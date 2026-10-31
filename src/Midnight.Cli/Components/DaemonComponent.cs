using System.Text.Json;
using System.Text.Json.Nodes;
using Midnight.Cli.Core;

namespace Midnight.Cli.Components;

public class DaemonSettings
{
    public bool Ssl { get; set; }
    public string Address { get; set; } = "";
    public int Port { get; set; } = 8080;
}

/// <summary>
/// The daemon is a self contained binary (/usr/local/bin/midnight-daemon) built from source inside a .NET SDK
/// container and run by systemd. With SSL certbot provides the certificate.
/// </summary>
public static class DaemonComponent
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // ─── Installation ────────────────────────────────────────────────────────

    public static async Task InstallAsync(HostInfo host, InstallState state, DaemonSettings settings, string? localSource, string? configFile)
    {
        Ui.Section("Installing the daemon");

        await Ui.StepAsync("Installing base packages", () => Requirements.InstallBasePackagesAsync(host));
        await Ui.StepAsync("Installing Docker", Requirements.InstallDockerAsync);

        if (settings.Ssl)
            await Ui.StepAsync("Installing certbot", () => Requirements.InstallCertbotAsync(host));

        await Ui.StepAsync("Downloading the source code", () => SourceManager.FetchAsync(state, localSource));
        await Ui.StepAsync("Building the daemon (takes a few minutes)", () => BuildAsync(host));
        await Ui.StepAsync("Installing the systemd service", WriteUnitAsync);

        if (settings.Ssl)
            await Ui.StepAsync($"Requesting a certificate for {settings.Address}", () => RequestCertificateAsync(settings.Address, state.Panel != null));

        state.Daemon = new DaemonState { Ssl = settings.Ssl, Address = settings.Address, Port = settings.Port };
        state.Save();

        // Reinstall: apply the new port/ssl settings to the existing configuration and restart onto the new binary
        await ConfigureAsync(state.Daemon, configFile ?? (File.Exists(Paths.DaemonConfig) ? Paths.DaemonConfig : null));
    }

    public static async Task UpdateAsync(HostInfo host, InstallState state)
    {
        Ui.Section("Updating the daemon");

        await Ui.StepAsync("Downloading the latest source code", () => SourceManager.FetchAsync(state, null));
        await Ui.StepAsync("Building the daemon", () => BuildAsync(host));

        if (File.Exists(Paths.DaemonConfig))
        {
            await Ui.StepAsync("Restarting the daemon", () => Shell.RunAsync("systemctl", "restart", Paths.DaemonService));
            await Ui.StepAsync("Waiting for the daemon", () => WaitForHealthAsync(state.Daemon!));
        }

        state.Save();
    }

    public static async Task UninstallAsync(InstallState state, bool deleteData)
    {
        Ui.Section("Uninstalling the daemon");

        await Ui.StepAsync("Stopping the service", async () =>
        {
            await Shell.RunAsync("systemctl", ["disable", "--now", Paths.DaemonService], new ShellOptions { AllowFailure = true });
            DeleteFile(Paths.DaemonUnit);
            DeleteFile(Paths.DaemonBinary);
            await Shell.RunAsync("systemctl", "daemon-reload");
        });

        if (deleteData && !Shell.DryRun)
        {
            if (Directory.Exists(Paths.DaemonData))
                Directory.Delete(Paths.DaemonData, true);

            DeleteFile(Paths.DaemonConfig);
        }

        state.Daemon = null;
        state.Save();
    }

    // ─── Configuration from the panel ────────────────────────────────────────

    /// <summary>
    /// Writes /etc/midnight/config.json from the configuration of the panel's node setup tab,
    /// starts the daemon and checks that it reaches the panel.
    /// </summary>
    public static async Task ConfigureAsync(DaemonState daemon, string? configFile)
    {
        string? json;

        if (configFile != null)
        {
            json = ExtractJson(await File.ReadAllTextAsync(configFile)) ?? throw new InstallerException($"{configFile} contains no valid json");
        }
        else if (!Ui.Interactive)
        {
            Ui.Warning("No --daemon-config given. Run [bold]midnight configure daemon[/] once the node exists in the panel.");
            return;
        }
        else
        {
            json = ReadPastedConfiguration();

            if (json == null)
            {
                Ui.Warning("Skipped. Run [bold]midnight configure daemon[/] once the node exists in the panel.");
                return;
            }
        }

        var merged = MergeConfiguration(json, daemon);

        await Ui.StepAsync($"Writing {Paths.DaemonConfig}", async () =>
        {
            if (Shell.DryRun)
                return;

            Directory.CreateDirectory(Paths.EtcDir);

            // The file holds the node token: created as 0600, an existing file is restricted before it gets the new token
            if (File.Exists(Paths.DaemonConfig))
                File.SetUnixFileMode(Paths.DaemonConfig, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            await using (var stream = new FileStream(Paths.DaemonConfig, new FileStreamOptions
                         {
                             Mode = FileMode.Create, Access = FileAccess.Write,
                             UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                         }))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(merged);

            File.SetUnixFileMode(Paths.DaemonConfig, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        });

        await Ui.StepAsync("Starting the daemon", () => Shell.RunAsync("systemctl", "restart", Paths.DaemonService));
        await Ui.StepAsync("Waiting for the daemon", () => WaitForHealthAsync(daemon));

        await Task.Delay(Shell.DryRun ? 0 : 2000);

        if (await IsConnectedToPanelAsync())
            Ui.Success("The daemon is connected to the panel");
        else
            Ui.Warning("The daemon runs but hasn't reached the panel yet. Check [bold]midnight logs daemon[/]");
    }

    private static string? ReadPastedConfiguration()
    {
        Ui.Section("Connect to the panel");
        Spectre.Console.AnsiConsole.MarkupLine("  Paste the setup command or [bold]config.json[/] from the node's setup tab in the panel.");
        Ui.Hint("Finish with an empty line. Press enter right away to skip and run midnight configure daemon later.");
        Spectre.Console.AnsiConsole.WriteLine();

        var input = new System.Text.StringBuilder();

        while (Console.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                if (input.Length == 0)
                    return null;

                if (ExtractJson(input.ToString()) is { } json)
                    return json;

                continue;
            }

            input.AppendLine(line);
        }

        return ExtractJson(input.ToString());
    }

    /// <summary>Accepts raw json or the setup command (json inside a heredoc) and returns the json object</summary>
    internal static string? ExtractJson(string input)
    {
        if (TryParseObject(input.Trim()) is { } direct)
            return direct;

        var start = input.IndexOf('{');
        var end = input.LastIndexOf('}');

        return start >= 0 && end > start ? TryParseObject(input[start..(end + 1)]) : null;
    }

    /// <summary>Takes the panel's configuration and applies the local settings (port, ssl paths, data path)</summary>
    internal static string MergeConfiguration(string json, DaemonState daemon)
    {
        var config = JsonNode.Parse(json)!.AsObject();

        foreach (var key in new[] { "panelUrl", "tokenId", "token" })
        {
            if (string.IsNullOrWhiteSpace(config[key]?.GetValue<string>()))
                throw new InstallerException($"The configuration has no '{key}', copy it again from the node setup tab");
        }

        var certificates = Paths.CertificateDir(daemon.Address);

        config["httpPort"] = daemon.Port;
        config["dataPath"] = Paths.DaemonData;
        config["dockerSocket"] ??= "unix:///var/run/docker.sock";
        config["ssl"] = new JsonObject
        {
            ["enabled"] = daemon.Ssl,
            ["certificatePath"] = daemon.Ssl ? $"{certificates}/fullchain.pem" : "",
            ["keyPath"] = daemon.Ssl ? $"{certificates}/privkey.pem" : ""
        };

        return config.ToJsonString(Indented);
    }

    // ─── Operations ──────────────────────────────────────────────────────────

    public static async Task<bool> IsHealthyAsync(DaemonState daemon)
    {
        try
        {
            if (Shell.DryRun)
                return true;

            // The certificate is issued for the domain, so connect to localhost but validate against the domain
            using var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                    await socket.ConnectAsync(System.Net.IPAddress.Loopback, context.DnsEndPoint.Port, cancellationToken);
                    return new System.Net.Sockets.NetworkStream(socket, true);
                }
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };

            return (await http.GetAsync($"{daemon.Url}/api/health")).IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<bool> IsConnectedToPanelAsync()
    {
        var journal = await Shell.RunAsync("journalctl", ["-u", Paths.DaemonService, "-n", "200", "--no-pager"], new ShellOptions { AllowFailure = true });
        return Shell.DryRun || journal.Output.Contains("Connected to the panel");
    }

    private static async Task WaitForHealthAsync(DaemonState daemon)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await IsHealthyAsync(daemon))
                return;

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        await Shell.RunAsync("journalctl", ["-u", Paths.DaemonService, "-n", "40", "--no-pager"], new ShellOptions { AllowFailure = true });
        throw new InstallerException("The daemon did not answer within 60 seconds");
    }

    private static async Task BuildAsync(HostInfo host)
    {
        var output = Path.Combine(Paths.SourceDir, "out", "daemon");

        await Shell.RunAsync("docker",
        [
            "run", "--rm",
            "-v", $"{Paths.SourceDir}:/src", "-w", "/src/demon",
            "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_NOLOGO=1",
            "mcr.microsoft.com/dotnet/sdk:10.0",
            "dotnet", "publish", "src/Midnight.Daemon", "-c", "Release", "-r", $"linux-{host.RuntimeArchitecture}",
            "--self-contained", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-o", "/src/out/daemon"
        ]);

        await Shell.RunAsync("install", "-m", "755", Path.Combine(output, "Midnight.Daemon"), Paths.DaemonBinary);
    }

    private static async Task WriteUnitAsync()
    {
        if (!Shell.DryRun)
        {
            Directory.CreateDirectory(Paths.DaemonData);
            Directory.CreateDirectory(Paths.EtcDir);
            File.SetUnixFileMode(Paths.EtcDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(Paths.DaemonUnit, BuildUnit());
        }

        await Shell.RunAsync("systemctl", "daemon-reload");
        await Shell.RunAsync("systemctl", "enable", Paths.DaemonService);
    }

    internal static string BuildUnit() => $"""
        [Unit]
        Description=Midnight Daemon
        Documentation=https://github.com/Midnight-Panel/demon
        After=network-online.target docker.service
        Wants=network-online.target
        Requires=docker.service

        [Service]
        Type=notify
        ExecStart={Paths.DaemonBinary}
        Environment=MIDNIGHT_DAEMON_CONFIG={Paths.DaemonConfig}
        WorkingDirectory={Paths.DaemonData}
        Restart=on-failure
        RestartSec=5
        User=root
        LimitNOFILE=1048576

        [Install]
        WantedBy=multi-user.target

        """;

    /// <summary>
    /// Certbot needs port 80. A panel on the same server holds it with its proxy, so the proxy is paused
    /// while certbot runs. Certbot stores the hooks and uses them again for the automatic renewals.
    /// </summary>
    public static Task RequestCertificateAsync(string domain, bool panelOnSameServer)
    {
        var arguments = new List<string>
        {
            "certonly", "--standalone", "--non-interactive", "--agree-tos", "--keep-until-expiring",
            "--register-unsafely-without-email", "-d", domain,
            "--deploy-hook", $"systemctl restart {Paths.DaemonService}"
        };

        if (panelOnSameServer)
        {
            arguments.AddRange(["--pre-hook", $"docker compose -f {PanelComponent.ComposeFile} stop proxy"]);
            arguments.AddRange(["--post-hook", $"docker compose -f {PanelComponent.ComposeFile} start proxy"]);
        }

        return Shell.RunAsync("certbot", [.. arguments]);
    }

    private static string? TryParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject node ? node.ToJsonString(Indented) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void DeleteFile(string path)
    {
        if (!Shell.DryRun && File.Exists(path))
            File.Delete(path);
    }
}
