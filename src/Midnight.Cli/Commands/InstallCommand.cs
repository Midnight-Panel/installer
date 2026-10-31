using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace Midnight.Cli.Commands;

public class InstallSettings : GlobalSettings
{
    [CommandArgument(0, "[target]")]
    [Description("panel, demon or all. Without it an interactive menu is shown.")]
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

    [CommandOption("--demon-address <HOST>")]
    [Description("Domain or IP of this node")]
    public string? DemonAddress { get; init; }

    [CommandOption("--demon-port <PORT>")]
    [Description("Demon port (default 8080)")]
    public int? DemonPort { get; init; }

    [CommandOption("--demon-config <FILE>")]
    [Description("config.json or setup command from the node setup tab of the panel")]
    public string? DemonConfig { get; init; }

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
        Ui.Banner($"Installer v{AppInfo.Version}");

        var host = await HostInfo.DetectAsync();

        if (!settings.DryRun)
            host.EnsureSupported();

        PrintHost(host, state);

        var target = settings.Target?.ToLowerInvariant() ?? AskTarget(state);

        if (target is "update" or "configure" or "uninstall")
            return await RunMaintenanceAsync(target, host, state);

        var (installPanel, installDemon) = target switch
        {
            "panel" => (true, false),
            "demon" => (false, true),
            "all" => (true, true),
            _ => throw new InstallerException($"Unknown component '{target}', use panel, demon or all")
        };

        if (installPanel && state.Panel != null && !Ui.Confirm("The panel is already installed. Reinstall it (data is kept)?", false))
            return 0;

        if (installDemon && state.Demon != null && !Ui.Confirm("The demon is already installed. Reinstall it (server files are kept)?", false))
            return 0;

        var panel = installPanel ? await CollectPanelAsync(settings, host) : null;
        var demon = installDemon ? await CollectDemonAsync(settings, host, panel) : null;
        var source = settings.Source != null ? Path.GetFullPath(settings.Source) : null;

        if (settings.DemonConfig != null && !File.Exists(settings.DemonConfig))
            throw new InstallerException($"{settings.DemonConfig} does not exist");

        if (source != null && !Directory.Exists(Path.Combine(source, "panel")))
            throw new InstallerException($"{source} must contain the panel and demon folders");

        PrintSummary(panel, demon, source);

        if (!settings.Yes && !Ui.Confirm("Start the installation?"))
            return 0;

        if (panel != null)
            await PanelComponent.InstallAsync(host, state, panel, source);

        if (demon != null)
            await DemonComponent.InstallAsync(host, state, demon, source, settings.DemonConfig);

        await Ui.StepAsync("Installing the midnight command", SelfInstall.InstallAsync);

        PrintFinish(panel, demon);
        return 0;
    }

    // ─── Menu ────────────────────────────────────────────────────────────────

    private static string AskTarget(InstallState state)
    {
        var options = new List<(string Key, string Label, string Description)>
        {
            ("panel", "Panel", "web interface, database and HTTPS proxy"),
            ("demon", "Demon", "turns this server into a node for game servers"),
            ("all", "Panel + Demon", "everything on this one server")
        };

        if (state.HasAnything)
        {
            options.Add(("update", "Update", "pull the latest version and restart"));
            if (state.Demon != null)
                options.Add(("configure", "Configure demon", "paste a new configuration from the panel"));
            options.Add(("uninstall", "Uninstall", "remove the panel or the demon"));
        }

        options.Add(("exit", "Exit", ""));

        AnsiConsole.WriteLine();
        var choice = Ui.Choose("What do you want to install?", options, x => x.Label, x => x.Description.Length > 0 ? x.Description : null).Key;

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
                if (state.Demon != null)
                    await DemonComponent.UpdateAsync(host, state);
                Ui.Success("Update finished");
                break;

            case "configure":
                RequireDemon(state);
                await DemonComponent.ConfigureAsync(state.Demon!, null);
                break;

            case "uninstall":
                var targets = new List<string>();
                if (state.Panel != null) targets.Add("panel");
                if (state.Demon != null) targets.Add("demon");

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

            if (!Validation.IsPort(panel.HttpPort))
                throw new InstallerException($"{panel.HttpPort} is no valid port (1-65535)");
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

        if (!Validation.IsUsername(panel.AdminUsername))
            throw new InstallerException($"'{panel.AdminUsername}' is no valid username (3-32 characters: letters, numbers, . _ -)");

        panel.AdminPassword = Environment.GetEnvironmentVariable("MIDNIGHT_ADMIN_PASSWORD") ?? AskNewPassword();

        if (panel.AdminPassword.Length < 8)
            throw new InstallerException("The administrator password needs at least 8 characters");

        panel.Registration = options.Registration ?? Ui.Confirm("Allow anyone to register an account?", false);

        return panel;
    }

    private static async Task<DemonSettings> CollectDemonAsync(InstallSettings options, HostInfo host, PanelSettings? panel)
    {
        Ui.Section("Demon");

        var demon = new DemonSettings
        {
            Ssl = options.Ssl ?? Ui.Confirm("Use SSL for the demon (required when the panel uses https)?", panel?.Ssl ?? true)
        };

        if (panel is { Ssl: true } && !demon.Ssl)
            Ui.Warning("Browsers block connections from an https panel to a demon without SSL (console, files).");

        demon.Address = await AskAddressAsync("Address of this node", demon.Ssl, options.DemonAddress, host, "--demon-address");
        demon.Port = options.DemonPort ?? Ui.AskPort("Demon port", 8080, "--demon-port");

        if (!Validation.IsPort(demon.Port))
            throw new InstallerException($"{demon.Port} is no valid port (1-65535)");

        Ui.Hint($"Create the node in the panel with FQDN [bold]{Markup.Escape(demon.Address)}[/], port {demon.Port} and SSL {(demon.Ssl ? "on" : "off")}.");
        Ui.Hint("Its setup tab shows the configuration you paste at the end of the installation.");

        return demon;
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
        static string Installed(bool yes) => yes ? "[green]● installed[/]" : "[grey]○ not installed[/]";

        Ui.Box("This server", Ui.KeyValues(
        [
            ("System", $"[bold]{Markup.Escape(host.OsName)}[/] [grey]·[/] {host.Architecture} [grey]·[/] {host.MemoryGb:0.0} GB RAM"),
            ("Public IP", host.PublicIp.Length > 0 ? $"[bold]{Markup.Escape(host.PublicIp)}[/]" : "[grey]unknown[/]"),
            ("Panel", Installed(state.Panel != null)),
            ("Demon", Installed(state.Demon != null)),
            ("Log file", $"[grey]{Markup.Escape(InstallerLog.Path)}[/]")
        ]));
    }

    private static void PrintSummary(PanelSettings? panel, DemonSettings? demon, string? source)
    {
        AnsiConsole.WriteLine();
        var rows = new List<(string, string)>();

        if (panel != null)
        {
            rows.Add(("Panel URL", $"[bold]{Markup.Escape(panel.Url)}[/]"));
            rows.Add(("SSL", panel.Ssl ? "yes, Let's Encrypt via Caddy (auto renew)" : "no"));
            rows.Add(("Administrator", Markup.Escape($"{panel.AdminUsername} <{panel.AdminEmail}>")));
            rows.Add(("Registration", panel.Registration ? "open for everyone" : "administrators only"));
            rows.Add(("Database", "PostgreSQL 17 (docker)"));
        }

        if (demon != null)
        {
            if (panel != null)
                rows.Add(("", ""));

            rows.Add(("Demon", $"[bold]{Markup.Escape($"{(demon.Ssl ? "https" : "http")}://{demon.Address}:{demon.Port}")}[/]"));
            rows.Add(("SSL", demon.Ssl ? "yes, Let's Encrypt via certbot (auto renew)" : "no"));
            rows.Add(("Server data", Paths.DemonData));
        }

        rows.Add(("", ""));
        rows.Add(("Source", Markup.Escape(source ?? $"{Sources.PanelRepository}, {Sources.DemonRepository} ({Sources.Branch})")));

        Ui.Summary(rows);
        AnsiConsole.WriteLine();
    }

    private static void PrintFinish(PanelSettings? panel, DemonSettings? demon)
    {
        var rows = new List<(string, string)>();
        var next = new List<string>();

        if (panel != null)
        {
            rows.Add(("Panel", $"[bold {Ui.Brand} link]{Markup.Escape(panel.Url)}[/]"));
            rows.Add(("Sign in", Markup.Escape(panel.AdminEmail)));

            if (panel.Ssl)
                next.Add("Open the panel, the certificate is requested on the first visit (a few seconds).");
        }

        if (demon != null)
        {
            if (rows.Count > 0)
                rows.Add(("", ""));

            rows.Add(("Demon", $"[bold]{Markup.Escape($"{(demon.Ssl ? "https" : "http")}://{demon.Address}:{demon.Port}")}[/]"));

            if (!File.Exists(Paths.DemonConfig))
                next.Add("Create the node in the panel, then run [bold]midnight configure demon[/].");

            next.Add($"Open port {demon.Port} and the ports of your game servers in the firewall.");
        }

        var content = new List<IRenderable> { Ui.KeyValues(rows) };

        if (next.Count > 0)
        {
            content.Add(new Text(""));
            content.Add(new Markup("[bold]Next steps[/]"));

            // Own column for the numbers, so wrapped lines stay aligned with the text
            var steps = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(1)).AddColumn();
            foreach (var (step, index) in next.Select((step, index) => (step, index)))
                steps.AddRow($"[{Ui.Brand}]{index + 1}.[/]", step);
            content.Add(steps);
        }

        AnsiConsole.WriteLine();
        Ui.Box("✔ Installation complete", new Rows(content), Color.Green);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]Manage everything with the[/] [bold]midnight[/] [grey]command:[/]");
        AnsiConsole.WriteLine();

        var commands = new Grid().AddColumn(new GridColumn().PadLeft(4).PadRight(3).NoWrap()).AddColumn();
        commands.AddRow("[bold]midnight status[/]", "[grey]health of everything installed[/]");
        commands.AddRow("[bold]midnight logs panel -f[/]", "[grey]follow the logs[/]");
        commands.AddRow("[bold]midnight restart demon[/]", "[grey]start, stop or restart[/]");
        commands.AddRow("[bold]midnight update[/]", "[grey]update to the latest version[/]");
        commands.AddRow("[bold]midnight doctor[/]", "[grey]find and fix problems[/]");
        AnsiConsole.Write(commands);
        AnsiConsole.WriteLine();
    }
}
