using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

/// <summary>start, stop and restart share this command, the action comes from the command name</summary>
public class ServiceCommand : MidnightCommand<TargetSettings>
{
    protected override async Task<int> RunAsync(TargetSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var (panel, demon) = ResolveTarget(settings.Target, state);

        if (panel)
        {
            var arguments = CommandName switch { "start" => new[] { "up", "-d" }, "stop" => ["stop"], _ => ["restart"] };
            await Ui.StepAsync($"{Verb()} the panel", () => PanelComponent.ComposeAsync(arguments));
        }

        if (demon)
            await Ui.StepAsync($"{Verb()} the demon", () => Shell.RunAsync("systemctl", CommandName, Paths.DemonService));

        return 0;
    }

    private string Verb() => CommandName switch { "start" => "Starting", "stop" => "Stopping", _ => "Restarting" };
}
