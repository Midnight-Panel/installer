using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class UninstallSettings : GlobalSettings
{
    [CommandArgument(0, "<target>")]
    [Description("panel or demon")]
    public string Target { get; init; } = "";

    [CommandOption("--delete-data")]
    [Description("Also delete the database / server files (can't be undone)")]
    public bool? DeleteData { get; init; }
}

public class UninstallCommand : MidnightCommand<UninstallSettings>
{
    protected override async Task<int> RunAsync(UninstallSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        await UninstallAsync(settings.Target.ToLowerInvariant(), state, settings.DeleteData);
        return 0;
    }

    internal static async Task UninstallAsync(string target, InstallState state, bool? deleteData)
    {
        switch (target)
        {
            case "panel":
                if (state.Panel == null)
                    throw new InstallerException("The panel is not installed");

                await PanelComponent.UninstallAsync(state, deleteData ?? Ui.Confirm("Also delete the database and all panel data? This can't be undone.", false));
                break;

            case "demon":
                if (state.Demon == null)
                    throw new InstallerException("The demon is not installed");

                Ui.Warning("Game servers are containers and keep running until you remove them.");
                await DemonComponent.UninstallAsync(state, deleteData ?? Ui.Confirm($"Also delete {Paths.DemonData} (all server files) and the configuration?", false));
                break;

            default:
                throw new InstallerException("Use: midnight uninstall panel|demon");
        }

        Ui.Success($"Uninstalled the {target}");

        if (!state.HasAnything)
            Ui.Hint($"Nothing is installed anymore. Remove the cli with: rm {Paths.CliBinary}");
    }
}
