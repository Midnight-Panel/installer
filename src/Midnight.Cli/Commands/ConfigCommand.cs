using System.ComponentModel;
using System.Text.Json.Nodes;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Json;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class ConfigSettings : GlobalSettings
{
    [CommandArgument(0, "<target>")]
    [Description("panel or daemon")]
    public string Target { get; init; } = "";

    [CommandOption("-e|--edit")]
    [Description("Open the file in $EDITOR and restart afterwards")]
    public bool Edit { get; init; }
}

/// <summary>Shows the configuration with secrets hidden, or opens it in an editor</summary>
public class ConfigCommand : MidnightCommand<ConfigSettings>
{
    protected override async Task<int> RunAsync(ConfigSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        var editor = Environment.GetEnvironmentVariable("EDITOR") is { Length: > 0 } value ? value : "nano";

        switch (settings.Target.ToLowerInvariant())
        {
            case "panel":
                RequirePanel(state);

                if (settings.Edit)
                {
                    await Shell.RunAsync(editor, [PanelComponent.ComposeFile], new ShellOptions { Interactive = true });
                    await Ui.StepAsync("Applying the configuration", () => PanelComponent.ComposeAsync("up", "-d"));
                    return 0;
                }

                var panelConfig = await PanelComponent.ComposeAsync(["exec", "-T", "panel", "cat", "/app/storage/config.json"], new ShellOptions { AllowFailure = true });
                if (!panelConfig.Success)
                    throw new InstallerException("The panel container is not running");

                Print(panelConfig.Output, ("Security", "JwtSecret"), ("Database", "ConnectionString"));
                Ui.Hint($"Values in {PanelComponent.ComposeFile} (environment) override this file. Edit with: midnight config panel --edit");
                return 0;

            case "daemon":
                RequireDaemon(state);

                if (!File.Exists(Paths.DaemonConfig))
                    throw new InstallerException("No configuration yet, run: midnight configure daemon");

                if (settings.Edit)
                {
                    await Shell.RunAsync(editor, [Paths.DaemonConfig], new ShellOptions { Interactive = true });

                    if (DaemonComponent.ExtractJson(await File.ReadAllTextAsync(Paths.DaemonConfig, cancellationToken)) == null)
                        throw new InstallerException("The file is no valid json anymore, the daemon was not restarted");

                    await Ui.StepAsync("Restarting the daemon", () => Shell.RunAsync("systemctl", "restart", Paths.DaemonService));
                    return 0;
                }

                Print(await File.ReadAllTextAsync(Paths.DaemonConfig, cancellationToken), (null, "token"));
                return 0;

            default:
                throw new InstallerException("Use: midnight config panel|daemon [--edit]");
        }
    }

    private static void Print(string json, params (string? Section, string Key)[] secrets)
    {
        var node = JsonNode.Parse(json)!.AsObject();

        foreach (var (section, secret) in secrets)
        {
            var target = section == null ? node : node[section] as JsonObject;

            if (target?[secret] != null)
                target[secret] = "***";
        }

        AnsiConsole.Write(new JsonText(node.ToJsonString()));
        AnsiConsole.WriteLine();
    }
}
