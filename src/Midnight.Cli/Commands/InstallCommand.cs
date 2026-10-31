using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class InstallSettings : GlobalSettings
{
    [CommandArgument(0, "[target]")]
    [Description("panel, daemon or all. Without it an interactive menu is shown.")]
    public string? Target { get; init; }

    [CommandOption("--ssl")]
    [Description("Use https with a Let's Encrypt certificate (address must be a domain)")]
    public bool UseSsl { get; init; }

    [CommandOption("--no-ssl")]
    [Description("Use plain http (address can be an IP)")]
    public bool NoSsl { get; init; }

    public bool? Ssl => NoSsl ? false : UseSsl ? true : null;

    [CommandOption("--panel-address <HOST>")]
    [Description("Domain or IP of the panel")]
    public string? PanelAddress { get; init; }

    [CommandOption("--http-port <PORT>")]
    [Description("Panel port without SSL (default 80)")]
    public int? HttpPort { get; init; }

    [CommandOption("--admin-email <EMAIL>")]
    [Description("Email of the first administrator. The password is read from MIDNIGHT_ADMIN_PASSWORD.")]
    public string? AdminEmail { get; init; }

    [CommandOption("--admin-username <NAME>")]
    public string? AdminUsername { get; init; }

    [CommandOption("--registration")]
    [Description("Allow anyone to register an account")]
    public bool? Registration { get; init; }

    [CommandOption("--daemon-address <HOST>")]
    [Description("Domain or IP of this node")]
    public string? DaemonAddress { get; init; }

    [CommandOption("--daemon-port <PORT>")]
    [Description("Daemon port (default 8080)")]
    public int? DaemonPort { get; init; }

    [CommandOption("--daemon-config <FILE>")]
    [Description("config.json or setup command from the node setup tab of the panel")]
    public string? DaemonConfig { get; init; }

    [CommandOption("--source <DIR>")]
    [Description("Install from a local checkout containing panel/ and demon/ instead of git")]
    public string? Source { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Don't ask for confirmation")]
    public bool Yes { get; init; }
}

/// <summary>Interactive installation wizard (or unattended with options)</summary>
public class InstallCommand : MidnightCommand<InstallSettings>
{
    protected override async Task<int> RunAsync(InstallSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        Ui.Banner($"Installer v{AppInfo.Version} · open-source game & app server hosting");

        var host = await HostInfo.DetectAsync();

        if (!settings.DryRun)
            host.EnsureSupported();

        PrintHost(host, state);

        var target = settings.Target?.ToLowerInvariant() ?? AskTarget(state);

        if (target is "update" or "configure" or "uninstall")
            return await RunMaintenanceAsync(target, host, state);

        var (installPanel, installDaemon) = target switch
        {
            "panel" => (true, false),
            "daemon" => (false, true),
            "all" => (true, true),
            _ => throw new InstallerException($"Unknown component '{target}', use panel, daemon or all")
        };

        if (installPanel && state.Panel != null && !Ui.Confirm("The panel is already installed. Reinstall it (data is kept)?", false))
            return 0;

        if (installDaemon && state.Daemon != null && !Ui.Confirm("The daemon is already installed. Reinstall it (server files are kept)?", false))
            return 0;

        var panel = installPanel ? await CollectPanelAsync(settings, host) : null;
        var daemon = installDaemon ? await CollectDaemonAsync(settings, host, panel) : null;
        var source = settings.Source != null ? Path.GetFullPath(settings.Source) : null;

        if (settings.DaemonConfig != null && !File.Exists(settings.DaemonConfig))
            throw new InstallerException($"{settings.DaemonConfig} does not exist");

        if (source != null && !Directory.Exists(Path.Combine(source, "panel")))
            throw new InstallerException($"{source} must contain the panel and demon folders");

        PrintSummary(panel, daemon, source);

        if (!settings.Yes && !Ui.Confirm("Start the installation?"))
            return 0;

        if (panel != null)
            await PanelComponent.InstallAsync(host, state, panel, source);

        if (daemon != null)
            await DaemonComponent.InstallAsync(host, state, daemon, source, settings.DaemonConfig);

        await Ui.StepAsync("Installing the midnight command", SelfInstall.InstallAsync);

        PrintFinish(panel, daemon);
        return 0;
    }

    // ─── Menu ────────────────────────────────────────────────────────────────

    private static string AskTarget(InstallState state)
    {
        var options = new List<(string Key, string Label)>
        {
            ("panel", "Install the panel"),
            ("daemon", "Install the daemon (node)"),
            ("all", "Install panel + daemon on this server")
        };

        if (state.HasAnything)
        {
            options.Add(("update", "Update the installation"));
            if (state.Daemon != null)
                options.Add(("configure", "Paste a new daemon configuration"));
            options.Add(("uninstall", "Uninstall"));
        }

        options.Add(("exit", "Exit"));

        AnsiConsole.WriteLine();
        var choice = Ui.Choose("What do you want to do?", options, x => x.Label).Key;

        if (choice == "exit")
            throw new InstallerAbortedException();

        return choice;
    }

    private static async Task<int> RunMaintenanceAsync(string action, HostInfo host, InstallState state)
    {
        switch (action)
        {
            case "update":
                if (state.Panel != null)
                    await PanelComponent.UpdateAsync(state);
                if (state.Daemon != null)
                    await DaemonComponent.UpdateAsync(host, state);
                Ui.Success("Update finished");
                break;

            case "configure":
                await DaemonComponent.ConfigureAsync(state.Daemon!, null);
                break;

            case "uninstall":
                var targets = new List<string>();
                if (state.Panel != null) targets.Add("panel");
                if (state.Daemon != null) targets.Add("daemon");

                var target = Ui.Choose("What do you want to uninstall?", targets.Append("cancel"), x => x);
                if (target == "cancel")
                    return 0;

                await UninstallCommand.UninstallAsync(target, state, null);
                break;
        }

        return 0;
    }

    // ─── Questions ───────────────────────────────────────────────────────────

    private static async Task<PanelSettings> CollectPanelAsync(InstallSettings options, HostInfo host)
    {
        Ui.Section("Panel");

        var panel = new PanelSettings
        {
            Ssl = options.Ssl ?? Ui.Confirm("Use SSL (https with a free Let's Encrypt certificate)?")
        };

        panel.Address = await AskAddressAsync("Panel address", panel.Ssl, options.PanelAddress, host, "--panel-address");

        if (panel.Ssl)
        {
            foreach (var port in new[] { 80, 443 }.Where(HostInfo.IsPortInUse))
                Ui.Warning($"Port {port} is in use, the panel needs 80 and 443 for SSL");
        }
        else
        {
            panel.HttpPort = options.HttpPort ?? Ui.AskPort("HTTP port of the panel", 80, "--http-port");
        }

        Ui.Section("Administrator");
        Ui.Hint("The first account of the panel, it gets full access.");
        AnsiConsole.WriteLine();

        panel.AdminEmail = options.AdminEmail
                           ?? Ui.Ask("Email", validate: v => Validation.IsEmail(v) ? null : "Enter a valid email address", option: "--admin-email");

        if (!Validation.IsEmail(panel.AdminEmail))
            throw new InstallerException($"'{panel.AdminEmail}' is not a valid email address");

        panel.AdminUsername = options.AdminUsername
                              ?? Ui.Ask("Username", Validation.UsernameFromEmail(panel.AdminEmail), v => Validation.IsUsername(v) ? null : "3-32 characters: letters, numbers, . _ -");

        panel.AdminPassword = Environment.GetEnvironmentVariable("MIDNIGHT_ADMIN_PASSWORD") ?? AskNewPassword();

        if (panel.AdminPassword.Length < 8)
            throw new InstallerException("The administrator password needs at least 8 characters");

        panel.Registration = options.Registration ?? Ui.Confirm("Allow anyone to register an account?", false);

        return panel;
    }

    private static async Task<DaemonSettings> CollectDaemonAsync(InstallSettings options, HostInfo host, PanelSettings? panel)
    {
        Ui.Section("Daemon");

        var daemon = new DaemonSettings
        {
            Ssl = options.Ssl ?? Ui.Confirm("Use SSL for the daemon (required when the panel uses https)?", panel?.Ssl ?? true)
        };

        if (panel is { Ssl: true } && !daemon.Ssl)
            Ui.Warning("Browsers block connections from an https panel to a daemon without SSL (console, files).");

        daemon.Address = await AskAddressAsync("Address of this node", daemon.Ssl, options.DaemonAddress, host, "--daemon-address");
        daemon.Port = options.DaemonPort ?? Ui.AskPort("Daemon port", 8080, "--daemon-port");

        Ui.Hint($"Create the node in the panel with FQDN [bold]{Markup.Escape(daemon.Address)}[/], port {daemon.Port} and SSL {(daemon.Ssl ? "on" : "off")}.");
        Ui.Hint("Its setup tab shows the configuration you paste at the end of the installation.");

        return daemon;
    }

    /// <summary>With SSL a domain is required, without SSL an IP or domain. Domains are checked against the DNS.</summary>
    private static async Task<string> AskAddressAsync(string label, bool ssl, string? given, HostInfo host, string option)
    {
        while (true)
        {
            var address = (given ?? Ui.Ask(
                ssl ? $"{label} (domain, required for SSL)" : $"{label} (IP address or domain)",
                ssl ? null : host.PublicIp,
                v => Validation.IsAddress(v.ToLowerInvariant(), ssl) ? null : ssl ? "Enter a domain like panel.example.com (no http://, no port)" : "Enter an IPv4 address or domain (no http://, no port)",
                option)).ToLowerInvariant();

            if (!Validation.IsAddress(address, ssl))
                throw new InstallerException(ssl ? $"'{address}' is no domain, SSL needs a domain" : $"'{address}' is no valid IP address or domain");

            if (!Validation.IsDomain(address))
                return address;

            // Let's Encrypt only issues the certificate if the domain points to this server
            var resolved = await HostInfo.ResolveAsync(address);

            if (resolved == host.PublicIp)
            {
                Ui.Success($"{address} points to this server");
                return address;
            }

            Ui.Warning(resolved == null
                ? $"{address} does not resolve yet. Create an A record pointing to {host.PublicIp}."
                : $"{address} points to {resolved}, this server is {host.PublicIp}.");

            if (!ssl || given != null || Ui.Confirm("Continue anyway (DNS not updated yet, NAT or proxy)?", false))
                return address;
        }
    }

    private static string AskNewPassword()
    {
        while (true)
        {
            var password = Ui.AskSecret("Password (min. 8 characters)", v => v.Length >= 8 ? null : "The password needs at least 8 characters", "MIDNIGHT_ADMIN_PASSWORD");

            if (Ui.AskSecret("Repeat password") == password)
                return password;

            Ui.Warning("The passwords do not match");
        }
    }

    // ─── Output ──────────────────────────────────────────────────────────────

    private static void PrintHost(HostInfo host, InstallState state)
    {
        Ui.Info($"System     [bold]{Markup.Escape(host.OsName)}[/] · {host.Architecture} · {host.MemoryGb:0.0} GB RAM");
        Ui.Info($"Public IP  [bold]{host.PublicIp}[/]");
        Ui.Info($"Installed  Panel {(state.Panel != null ? "[green]yes[/]" : "[grey]no[/]")} · Daemon {(state.Daemon != null ? "[green]yes[/]" : "[grey]no[/]")}");
        Ui.Hint($"Log file: {Markup.Escape(InstallerLog.Path)}");
    }

    private static void PrintSummary(PanelSettings? panel, DaemonSettings? daemon, string? source)
    {
        Ui.Section("Summary");
        var rows = new List<(string, string)>();

        if (panel != null)
        {
            rows.Add(("Panel URL", $"[bold]{Markup.Escape(panel.Url)}[/]"));
            rows.Add(("SSL", panel.Ssl ? "yes, Let's Encrypt via Caddy (auto renew)" : "no"));
            rows.Add(("Administrator", Markup.Escape($"{panel.AdminUsername} <{panel.AdminEmail}>")));
            rows.Add(("Registration", panel.Registration ? "open for everyone" : "administrators only"));
            rows.Add(("Database", "PostgreSQL 17 (docker)"));
        }

        if (daemon != null)
        {
            if (panel != null)
                rows.Add(("", ""));

            rows.Add(("Daemon", $"[bold]{Markup.Escape($"{(daemon.Ssl ? "https" : "http")}://{daemon.Address}:{daemon.Port}")}[/]"));
            rows.Add(("SSL", daemon.Ssl ? "yes, Let's Encrypt via certbot (auto renew)" : "no"));
            rows.Add(("Server data", Paths.DaemonData));
        }

        rows.Add(("", ""));
        rows.Add(("Source", Markup.Escape(source ?? $"{Sources.PanelRepository}, {Sources.DaemonRepository} ({Sources.Branch})")));

        Ui.Summary(rows);
        AnsiConsole.WriteLine();
    }

    private static void PrintFinish(PanelSettings? panel, DaemonSettings? daemon)
    {
        Ui.Section("Done");

        if (panel != null)
        {
            Ui.Success($"Panel: [bold link]{Markup.Escape(panel.Url)}[/]");
            Ui.Hint($"Sign in as {Markup.Escape(panel.AdminEmail)}");
            if (panel.Ssl)
                Ui.Hint("The certificate is requested on the first visit, that can take a few seconds.");
        }

        if (daemon != null)
        {
            Ui.Success($"Daemon: {Markup.Escape($"{(daemon.Ssl ? "https" : "http")}://{daemon.Address}:{daemon.Port}")}");
            if (!File.Exists(Paths.DaemonConfig))
                Ui.Hint("Next: create the node in the panel and run [bold]midnight configure daemon[/]");
            Ui.Hint($"Open port {daemon.Port} and the ports of your game servers in your firewall.");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]Manage everything with the[/] [bold]midnight[/] [grey]command:[/]");
        Ui.Hint("midnight status · midnight logs panel -f · midnight restart daemon · midnight update · midnight --help");
        AnsiConsole.WriteLine();
    }
}
