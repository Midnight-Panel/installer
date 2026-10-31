using System.Security.Cryptography;
using System.Text;
using Midnight.Cli.Core;

namespace Midnight.Cli.Components;

/// <summary>Answers collected for a panel installation</summary>
public class PanelSettings
{
    public bool Ssl { get; set; }
    public string Address { get; set; } = "";
    public int HttpPort { get; set; } = 80;
    public bool Registration { get; set; }
    public string AdminEmail { get; set; } = "";
    public string AdminUsername { get; set; } = "";
    public string AdminPassword { get; set; } = "";

    public string Url => new PanelState { Ssl = Ssl, Address = Address, HttpPort = HttpPort }.Url;
}

/// <summary>
/// The panel runs as docker compose stack in /opt/midnight/panel: panel (built from source),
/// PostgreSQL and Caddy as reverse proxy, which also gets the Let's Encrypt certificate.
/// </summary>
public static class PanelComponent
{
    public static string ComposeFile => Path.Combine(Paths.PanelDir, "docker-compose.yml");

    // ─── Installation ────────────────────────────────────────────────────────

    public static async Task InstallAsync(HostInfo host, InstallState state, PanelSettings settings, string? localSource)
    {
        Ui.Section("Installing the panel");

        await Ui.StepAsync("Installing base packages", () => Requirements.InstallBasePackagesAsync(host));
        await Ui.StepAsync("Installing Docker", Requirements.InstallDockerAsync);
        await Ui.StepAsync("Downloading the source code", () => SourceManager.FetchAsync(state, localSource));
        await Ui.StepAsync("Writing the configuration", () => WriteStackAsync(settings));
        await Ui.StepAsync("Building the panel (takes a few minutes)", BuildAsync);
        await Ui.StepAsync("Starting panel, database and proxy", () => ComposeAsync("up", "-d", "--remove-orphans"));
        await Ui.StepAsync("Waiting for the panel", WaitForHealthAsync);
        await Ui.StepAsync($"Creating the administrator {settings.AdminUsername}",
            () => CreateAdminAsync(settings.AdminEmail, settings.AdminUsername, settings.AdminPassword));

        state.Panel = new PanelState
        {
            Ssl = settings.Ssl,
            Address = settings.Address,
            HttpPort = settings.HttpPort,
            Registration = settings.Registration,
            AdminEmail = settings.AdminEmail
        };
        state.Save();
    }

    public static async Task UpdateAsync(InstallState state)
    {
        Ui.Section("Updating the panel");

        await Ui.StepAsync("Downloading the latest source code", () => SourceManager.FetchAsync(state, null));
        await Ui.StepAsync("Rebuilding the panel", BuildAsync);
        await Ui.StepAsync("Restarting", () => ComposeAsync("up", "-d", "--remove-orphans"));
        await Ui.StepAsync("Waiting for the panel", WaitForHealthAsync);
        await Ui.StepAsync("Removing old images", () => Shell.RunAsync("docker", "image", "prune", "-f"));

        state.Save();
    }

    public static async Task UninstallAsync(InstallState state, bool deleteData)
    {
        Ui.Section("Uninstalling the panel");

        var arguments = new List<string> { "down", "--rmi", "local" };
        if (deleteData)
            arguments.Add("--volumes");

        await Ui.StepAsync("Removing the containers", async () =>
        {
            await ComposeAsync([.. arguments]);

            // "--rmi local" keeps images with a custom tag, so the locally built panel image is removed explicitly
            await Shell.RunAsync("docker", ["image", "rm", "midnight/panel:local"], new ShellOptions { AllowFailure = true });
        });

        if (deleteData && !Shell.DryRun && Directory.Exists(Paths.PanelDir))
            Directory.Delete(Paths.PanelDir, true);

        state.Panel = null;
        state.Save();
    }

    // ─── Operations ──────────────────────────────────────────────────────────

    public static Task<ShellResult> ComposeAsync(params string[] arguments)
        => ComposeAsync(arguments, null);

    public static Task<ShellResult> ComposeAsync(string[] arguments, ShellOptions? options)
        => Shell.RunAsync("docker", ["compose", "--project-directory", Paths.PanelDir, "-f", ComposeFile, .. arguments], options);

    public static Task BuildAsync() => ComposeAsync("build", "--pull", "panel");

    /// <summary>Asks the panel for /api/health from inside its docker network (the panel port isn't published)</summary>
    public static async Task<bool> IsHealthyAsync()
    {
        var result = await Shell.RunAsync("docker",
            ["run", "--rm", "--network", Paths.PanelNetwork, "busybox:stable", "wget", "-qO-", "-T", "3", "http://panel:8080/api/health"],
            new ShellOptions { AllowFailure = true });

        return result.Success && (Shell.DryRun || result.Output.Contains("ok"));
    }

    public static async Task WaitForHealthAsync()
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            if (await IsHealthyAsync())
                return;

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        await ComposeAsync(["logs", "--tail", "50", "panel"], new ShellOptions { AllowFailure = true });
        throw new InstallerException("The panel did not become healthy within 3 minutes");
    }

    /// <summary>Runs "create-admin" in a one off panel container. The password goes through the environment only.</summary>
    public static async Task CreateAdminAsync(string email, string? username, string password)
    {
        var arguments = new List<string> { "run", "--rm", "-T", "-e", "MIDNIGHT_ADMIN_PASSWORD", "panel", "create-admin", "--email", email };

        if (!string.IsNullOrEmpty(username))
            arguments.AddRange(["--username", username]);

        await ComposeAsync([.. arguments], new ShellOptions
        {
            Environment = new() { ["MIDNIGHT_ADMIN_PASSWORD"] = password },
            Secrets = [password]
        });
    }

    // ─── Files ───────────────────────────────────────────────────────────────

    private static async Task WriteStackAsync(PanelSettings settings)
    {
        if (Shell.DryRun)
        {
            InstallerLog.Write(BuildCompose(settings));
            InstallerLog.Write(BuildCaddyfile(settings));
            return;
        }

        Directory.CreateDirectory(Paths.PanelDir);

        var envFile = Path.Combine(Paths.PanelDir, ".env");
        if (!File.Exists(envFile))
        {
            // Created as 0600 right away, the password is never readable for other users
            await using (var stream = new FileStream(envFile, new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew, Access = FileAccess.Write,
                             UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                         }))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync($"POSTGRES_PASSWORD={RandomSecret(40)}\n");

            File.SetUnixFileMode(envFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await File.WriteAllTextAsync(ComposeFile, BuildCompose(settings));
        await File.WriteAllTextAsync(Path.Combine(Paths.PanelDir, "Caddyfile"), BuildCaddyfile(settings));
    }

    internal static string BuildCaddyfile(PanelSettings settings)
    {
        var builder = new StringBuilder();

        if (settings.Ssl)
        {
            builder.AppendLine("{");
            builder.AppendLine($"\temail {settings.AdminEmail}");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine($"{settings.Address} {{");
        }
        else
        {
            builder.AppendLine($":{settings.HttpPort} {{");
        }

        builder.AppendLine("\tencode zstd gzip");
        builder.AppendLine("\treverse_proxy panel:8080");
        builder.AppendLine("}");

        return builder.ToString();
    }

    internal static string BuildCompose(PanelSettings settings)
    {
        var ports = settings.Ssl
            ? """
                    - "80:80"
                    - "443:443"
                    - "443:443/udp"
              """
            : $"""
                    - "{settings.HttpPort}:{settings.HttpPort}"
              """;

        return $$"""
            # Generated by the Midnight installer. Change it with "midnight config panel --edit".
            name: midnight

            services:
              panel:
                build: {{Paths.SourceDir}}/panel
                image: midnight/panel:local
                restart: unless-stopped
                environment:
                  MIDNIGHT_PublicUrl: {{settings.Url}}
                  MIDNIGHT_Database__Provider: PostgreSQL
                  MIDNIGHT_Database__ConnectionString: Host=db;Database=midnight;Username=midnight;Password=${POSTGRES_PASSWORD}
                  MIDNIGHT_Authentication__EnableRegistration: "{{(settings.Registration ? "true" : "false")}}"
                  MIDNIGHT_Security__TrustedProxies__0: {{Paths.PanelSubnet}}
                volumes:
                  - panel-storage:/app/storage
                depends_on:
                  db:
                    condition: service_healthy
                networks: [midnight]

              db:
                image: postgres:17-alpine
                restart: unless-stopped
                environment:
                  POSTGRES_DB: midnight
                  POSTGRES_USER: midnight
                  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
                volumes:
                  - db-data:/var/lib/postgresql/data
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready -U midnight -d midnight"]
                  interval: 5s
                  timeout: 5s
                  retries: 30
                networks: [midnight]

              proxy:
                image: caddy:2-alpine
                restart: unless-stopped
                ports:
            {{ports}}
                volumes:
                  - ./Caddyfile:/etc/caddy/Caddyfile:ro
                  - caddy-data:/data
                  - caddy-config:/config
                depends_on: [panel]
                networks: [midnight]

            volumes:
              panel-storage:
              db-data:
              caddy-data:
              caddy-config:

            networks:
              midnight:
                ipam:
                  config:
                    - subnet: {{Paths.PanelSubnet}}

            """;
    }

    private static string RandomSecret(int length)
        => RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", length);
}
