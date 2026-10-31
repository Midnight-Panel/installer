using System.ComponentModel;
using Midnight.Cli.Components;
using Midnight.Cli.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Midnight.Cli.Commands;

public class CreateAdminSettings : GlobalSettings
{
    [CommandOption("--email <EMAIL>")]
    public string? Email { get; init; }

    [CommandOption("--username <NAME>")]
    public string? Username { get; init; }
}

/// <summary>Creates another administrator (or gives an existing account admin rights)</summary>
public class CreateAdminCommand : MidnightCommand<CreateAdminSettings>
{
    protected override async Task<int> RunAsync(CreateAdminSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        RequirePanel(state);

        var email = settings.Email ?? Ui.Ask("Email", validate: v => Validation.IsEmail(v) ? null : "Enter a valid email address", option: "--email");
        var username = settings.Username ?? Ui.Ask("Username", Validation.UsernameFromEmail(email), v => Validation.IsUsername(v) ? null : "3-32 characters: letters, numbers, . _ -", "--username");
        var password = Environment.GetEnvironmentVariable("MIDNIGHT_ADMIN_PASSWORD")
                       ?? Ui.AskSecret("Password (min. 8 characters)", v => v.Length >= 8 ? null : "The password needs at least 8 characters", "MIDNIGHT_ADMIN_PASSWORD");

        await Ui.StepAsync($"Creating the administrator {username}", () => PanelComponent.CreateAdminAsync(email, username, password));
        return 0;
    }
}

public class BackupSettings : GlobalSettings
{
    [CommandArgument(0, "[directory]")]
    [Description($"Target directory (default {Paths.BackupDir})")]
    public string? Directory { get; init; }
}

/// <summary>Database dump plus the panel storage volume (config, keys, avatars)</summary>
public class BackupCommand : MidnightCommand<BackupSettings>
{
    protected override async Task<int> RunAsync(BackupSettings settings, InstallState state, CancellationToken cancellationToken)
    {
        RequirePanel(state);

        var directory = Path.GetFullPath(settings.Directory ?? Paths.BackupDir);
        var created = !System.IO.Directory.Exists(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var database = Path.Combine(directory, $"midnight-db-{stamp}.dump");
        var storage = $"midnight-storage-{stamp}.tar.gz";

        if (!Shell.DryRun)
        {
            System.IO.Directory.CreateDirectory(directory);

            // Only restrict a directory we created, an existing one (e.g. /tmp or a home) keeps its permissions
            if (created || directory == Paths.BackupDir)
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await Ui.StepAsync("Dumping the database", async () =>
        {
            // Dump inside the container to a temp file, then copy it out (keeps binary output away from the log)
            await PanelComponent.ComposeAsync("exec", "-T", "db", "pg_dump", "-U", "midnight", "-d", "midnight", "-Fc", "-f", "/tmp/midnight.dump");
            await PanelComponent.ComposeAsync("cp", "db:/tmp/midnight.dump", database);
            await PanelComponent.ComposeAsync("exec", "-T", "db", "rm", "/tmp/midnight.dump");
        });

        await Ui.StepAsync("Archiving the panel storage", () => Shell.RunAsync("docker",
            "run", "--rm", "-v", "midnight_panel-storage:/data:ro", "-v", $"{directory}:/backup", "busybox:stable",
            "tar", "-czf", $"/backup/{storage}", "-C", "/data", "."));

        // The dump and the storage contain password hashes and keys
        if (!Shell.DryRun)
        {
            foreach (var file in new[] { database, Path.Combine(directory, storage) })
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Ui.Success($"Database: {Markup.Escape(database)}");
        Ui.Success($"Storage:  {Markup.Escape(Path.Combine(directory, storage))}");
        Ui.Hint("Restore the database with: pg_restore -U midnight -d midnight --clean <dump>");
        return 0;
    }
}
