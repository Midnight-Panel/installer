using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class LogsSettings : GlobalSettings
{
    [CommandArgument(0, "<target>")]
    [Description("panel, demon, db, proxy or installer")]
    public string Target { get; init; } = "";

    [CommandOption("-f|--follow")]
    [Description("Keep showing new lines")]
    public bool Follow { get; init; }

    [CommandOption("-n|--lines <LINES>")]
    [DefaultValue(100)]
    public int Lines { get; init; }
}

public class LogsCommand : MidnightCommand<LogsSettings>
{
    protected override async Task<int> RunAsync(LogsSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var interactive = new ShellOptions { Interactive = true, AllowFailure = true };
        var lines = settings.Lines.ToString();

        switch (settings.Target.ToLowerInvariant())
        {
            case "panel" or "db" or "database" or "proxy" or "caddy":
                RequirePanel(state);
                var service = settings.Target.ToLowerInvariant() switch { "database" => "db", "caddy" => "proxy", var x => x };
                await PanelComponent.ComposeAsync(["logs", "--tail", lines, .. Follow(settings, "-f"), service], interactive);
                break;

            case "demon":
                RequireDemon(state);
                await Shell.RunAsync("journalctl", ["-u", Paths.DemonService, "-n", lines, "--no-pager", .. Follow(settings, "-f")], interactive);
                break;

            case "installer":
                await Shell.RunAsync("tail", ["-n", lines, .. Follow(settings, "-f"), InstallerLog.Path], interactive);
                break;

            default:
                throw new InstallerException("Use: midnight logs panel|demon|db|proxy|installer [-f] [-n 100]");
        }

        return 0;
    }

    private static string[] Follow(LogsSettings settings, string flag) => settings.Follow ? [flag] : [];
}
