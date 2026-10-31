using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--dry-run")]
    [System.ComponentModel.Description("Show what would happen without changing the system (also works outside Linux)")]
    public bool DryRun { get; init; }
}

/// <summary>Common error handling: expected errors are shown as a message, not as a stack trace</summary>
public abstract class MidnightCommand<TSettings> : AsyncCommand<TSettings> where TSettings : GlobalSettings
{
    protected virtual bool RequiresRoot => true;

    /// <summary>The name the command was called with (start, stop, restart share one class)</summary>
    protected string CommandName { get; private set; } = "";

    public sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        CommandName = context.Name;
        Shell.DryRun = settings.DryRun;
        InstallerLog.Initialize(Paths.LogFile);

        try
        {
            if (RequiresRoot && !settings.DryRun && OperatingSystem.IsLinux() && !HostInfo.IsRoot)
                throw new InstallerException($"This command needs root: sudo midnight {string.Join(' ', context.Arguments)}");

            // The installer shows it in its banner
            if (settings.DryRun && this is not InstallCommand)
                Ui.Warning("[yellow]Dry run[/], nothing is changed. Commands are only written to the log.");

            return await RunAsync(settings, InstallState.Load(), cancellationToken);
        }
        catch (InstallerException e)
        {
            Ui.Error(Markup.Escape(e.Message));
            return 1;
        }
        catch (InstallerAbortedException)
        {
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            InstallerLog.Write(e.ToString());
            Ui.Error(Markup.Escape(e.Message));
            return 1;
        }
    }

    protected abstract Task<int> RunAsync(TSettings settings, InstallState state, CancellationToken cancellationToken);

    protected static void RequirePanel(InstallState state)
    {
        if (state.Panel == null)
            throw new InstallerException("The panel is not installed on this server");
    }

    protected static void RequireDaemon(InstallState state)
    {
        if (state.Daemon == null)
            throw new InstallerException("The daemon is not installed on this server");
    }

    /// <summary>"panel", "daemon" or "all" (everything installed)</summary>
    protected static (bool Panel, bool Daemon) ResolveTarget(string? target, InstallState state)
    {
        switch (target?.ToLowerInvariant() ?? "all")
        {
            case "panel":
                RequirePanel(state);
                return (true, false);
            case "daemon":
                RequireDaemon(state);
                return (false, true);
            case "all":
                if (!state.HasAnything)
                    throw new InstallerException("Nothing is installed yet. Run: sudo midnight install");
                return (state.Panel != null, state.Daemon != null);
            default:
                throw new InstallerException($"Unknown component '{target}', use panel, daemon or all");
        }
    }
}

public class TargetSettings : GlobalSettings
{
    [CommandArgument(0, "[target]")]
    [System.ComponentModel.Description("panel, daemon or all (default)")]
    public string? Target { get; init; }
}
