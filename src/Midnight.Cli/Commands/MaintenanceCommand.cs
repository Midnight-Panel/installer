using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class MaintenanceSettings : GlobalSettings
{
    [CommandArgument(0, "[action]")]
    [Description("on, off or status (default)")]
    public string Action { get; init; } = "status";
}

/// <summary>Shows the maintenance page to every visitor of the panel, e.g. during longer work on the server</summary>
public class MaintenanceCommand : MidnightCommand<MaintenanceSettings>
{
    protected override async Task<int> RunAsync(MaintenanceSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        RequirePanel(state);

        switch (settings.Action.ToLowerInvariant())
        {
            case "on":
                await Ui.StepAsync("Showing the maintenance page", () => PanelComponent.SetMaintenanceAsync(true));
                Ui.Hint("Every visitor sees the maintenance page now. Turn it off with: midnight maintenance off");
                return 0;

            case "off":
                await Ui.StepAsync("Hiding the maintenance page", () => PanelComponent.SetMaintenanceAsync(false));
                return 0;

            case "status":
                if (PanelComponent.MaintenanceEnabled)
                    Ui.Warning("Maintenance is [yellow]on[/], visitors see the maintenance page");
                else
                    Ui.Success("Maintenance is off, the panel is reachable");
                return 0;

            default:
                throw new InstallerException("Use: midnight maintenance on|off|status");
        }
    }
}
