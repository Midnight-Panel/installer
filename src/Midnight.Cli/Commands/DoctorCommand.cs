using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

/// <summary>Checks the installation for common problems and says how to fix them</summary>
public class DoctorCommand : MidnightCommand<GlobalSettings>
{
    protected override async Task<int> RunAsync(GlobalSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var problems = 0;

        async Task Check(string name, Func<Task<bool>> test, string fix)
        {
            bool passed;
            try { passed = await test(); }
            catch (Exception) { passed = false; }

            if (passed)
                Ui.Success(Markup.Escape(name));
            else
            {
                problems++;
                Ui.Warning($"{Markup.Escape(name)} [grey]– {Markup.Escape(fix)}[/]");
            }
        }

        Ui.Section("Checks");

        await Check("Docker is running", () => Shell.SucceedsAsync("systemctl", "is-active", "--quiet", "docker"), "systemctl start docker");
        await Check("Docker compose is available", () => Shell.SucceedsAsync("docker", "compose", "version"), "install the docker compose plugin");

        var free = new DriveInfo("/").AvailableFreeSpace / 1024d / 1024 / 1024;
        await Check($"Free disk space ({free:0} GB)", () => Task.FromResult(free > 5), "less than 5 GB free on /");

        if (state.Panel != null)
        {
            await Check("Panel answers", PanelComponent.IsHealthyAsync, "midnight logs panel");
            await Check("Database is healthy", async () => (await PanelComponent.ComposeAsync(["ps", "db"], new ShellOptions { AllowFailure = true })).Output.Contains("healthy"), "midnight logs db");
            await Check("Proxy is running", async () => (await PanelComponent.ComposeAsync(["ps", "proxy"], new ShellOptions { AllowFailure = true })).Output.Contains("Up"), "midnight logs proxy");
        }

        if (state.Demon != null)
        {
            var demon = state.Demon;
            await Check("Demon service is active", () => Shell.SucceedsAsync("systemctl", "is-active", "--quiet", Paths.DemonService), "midnight logs demon");
            await Check("Demon is configured", () => Task.FromResult(File.Exists(Paths.DemonConfig)), "midnight configure demon");
            await Check("Demon answers", () => DemonComponent.IsHealthyAsync(demon), "midnight logs demon");
            await Check("Demon reached the panel", DemonComponent.IsConnectedToPanelAsync, "check panel url and token: midnight config demon");

            if (demon.Ssl)
                await Check("Certificate valid for 14 more days",
                    () => Task.FromResult(StatusCommand.CertificateExpiry(demon.Address) is { } expiry && expiry > DateTime.Now.AddDays(14)),
                    "midnight ssl renew");
        }

        AnsiConsole.WriteLine();
        if (problems == 0)
            Ui.Success("[green]Everything looks good[/]");
        else
            Ui.Warning($"{problems} problem(s) found");

        return problems == 0 ? 0 : 1;
    }
}

public class SslSettings : GlobalSettings
{
    [CommandArgument(0, "<action>")]
    [System.ComponentModel.Description("renew")]
    public string Action { get; init; } = "";
}

public class SslCommand : MidnightCommand<SslSettings>
{
    protected override async Task<int> RunAsync(SslSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        if (settings.Action != "renew")
            throw new InstallerException("Use: midnight ssl renew");

        await Ui.StepAsync("Renewing certificates", () => Shell.RunAsync("certbot", "renew"));
        Ui.Hint("The panel certificate is renewed automatically by Caddy.");
        return 0;
    }
}
