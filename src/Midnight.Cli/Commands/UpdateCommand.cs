using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class UpdateCommand : MidnightCommand<TargetSettings>
{
    protected override async Task<int> RunAsync(TargetSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var (panel, demon) = ResolveTarget(settings.Target, state);
        var host = await HostInfo.DetectAsync();

        // Release channel: pick one of the newer releases, custom: newest code
        await VersionSelector.SelectForUpdateAsync(state, panel, demon, latest: false);

        if (panel)
            await PanelComponent.UpdateAsync(state);

        if (demon)
            await DemonComponent.UpdateAsync(host, state);

        Ui.Success("Update finished");
        Ui.Hint("Update the midnight command itself with: midnight self-update");
        return 0;
    }
}

public class SelfUpdateCommand : MidnightCommand<GlobalSettings>
{
    protected override async Task<int> RunAsync(GlobalSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var url = "";
        await Ui.StepAsync("Downloading the newest midnight cli", async () => url = await SelfInstall.UpdateAsync());
        Ui.Success($"Updated from {Spectre.Console.Markup.Escape(url)}");
        return 0;
    }
}

public class ConfigureSettings : GlobalSettings
{
    [CommandArgument(0, "<target>")]
    [Description("demon")]
    public string Target { get; init; } = "";

    [CommandOption("--file <FILE>")]
    [Description("Read the configuration from a file instead of pasting it")]
    public string? File { get; init; }
}

public class ConfigureCommand : MidnightCommand<ConfigureSettings>
{
    protected override async Task<int> RunAsync(ConfigureSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        if (settings.Target.ToLowerInvariant() != "demon")
            throw new InstallerException("Use: midnight configure demon");

        RequireDemon(state);
        await DemonComponent.ConfigureAsync(state.Demon!, settings.File);
        return 0;
    }
}

public class VersionCommand : MidnightCommand<GlobalSettings>
{
    protected override bool RequiresRoot => false;

    protected override Task<int> RunAsync(GlobalSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        Spectre.Console.AnsiConsole.MarkupLine($"midnight [bold]v{AppInfo.Version}[/]");
        return Task.FromResult(0);
    }
}
