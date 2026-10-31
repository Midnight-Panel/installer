using System.Security.Cryptography.X509Certificates;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;

namespace Midnight.Cli.Commands;

public class StatusCommand : MidnightCommand<GlobalSettings>
{
    protected override async Task<int> RunAsync(GlobalSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        if (!state.HasAnything)
        {
            Ui.Info("Nothing is installed on this server. Install with: [bold]sudo midnight install[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[grey]Component[/]").AddColumn("[grey]Status[/]").AddColumn("[grey]Details[/]");

        if (state.Panel != null)
        {
            table.AddRow("[bold]Panel[/]", Health(await PanelComponent.IsHealthyAsync()), Markup.Escape(state.Panel.Url));

            var containers = await PanelComponent.ComposeAsync(["ps", "--format", "{{.Service}}\t{{.State}}\t{{.Status}}"], new ShellOptions { AllowFailure = true });
            foreach (var line in containers.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length == 3)
                    table.AddRow($"  [grey]{Markup.Escape(parts[0])}[/]", parts[1] == "running" ? "[green]running[/]" : $"[red]{Markup.Escape(parts[1])}[/]", $"[grey]{Markup.Escape(parts[2])}[/]");
            }
        }

        if (state.Demon != null)
        {
            var service = (await Shell.RunAsync("systemctl", ["is-active", Paths.DemonService], new ShellOptions { AllowFailure = true })).Output.Trim();
            var details = Markup.Escape(state.Demon.Url);

            if (!File.Exists(Paths.DemonConfig))
                details += "\n[yellow]not configured, run: midnight configure demon[/]";
            else if (!await DemonComponent.IsConnectedToPanelAsync())
                details += "\n[yellow]no connection to the panel yet[/]";

            if (state.Demon.Ssl && CertificateExpiry(state.Demon.Address) is { } expiry)
                details += $"\n[grey]certificate valid until {expiry:yyyy-MM-dd}[/]";

            table.AddRow("[bold]Demon[/]", Health(await DemonComponent.IsHealthyAsync(state.Demon)), details);
            table.AddRow("  [grey]service[/]", service == "active" ? "[green]active[/]" : $"[red]{Markup.Escape(service)}[/]", "");
        }

        AnsiConsole.Write(table);
        Ui.Hint($"midnight v{AppInfo.Version} · installed with installer v{state.InstallerVersion}");
        return 0;
    }

    private static string Health(bool healthy) => healthy ? "[green]● healthy[/]" : "[red]● down[/]";

    internal static DateTime? CertificateExpiry(string domain)
    {
        try
        {
            var path = Path.Combine(Paths.CertificateDir(domain), "fullchain.pem");
            return File.Exists(path) ? X509Certificate2.CreateFromPemFile(path).NotAfter : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
