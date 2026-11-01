using Midnight.Cli.Core;

namespace Midnight.Cli.Components;

/// <summary>
/// Gets the panel and demon sources into /opt/midnight/src. Both are needed next to each other
/// because the demon builds against panel/src/Midnight.Shared.
/// </summary>
public static class SourceManager
{
    private static readonly string[] Repositories = ["panel", "demon"];
    private static bool _fetched;
    private static readonly HashSet<string> SkippedDirectories = ["node_modules", "bin", "obj", "storage", ".git", "wwwroot"];

    /// <param name="localSource">Directory with panel/ and demon/ to install from, null to use git</param>
    public static async Task FetchAsync(InstallState state, string? localSource)
    {
        if (_fetched)
            return;

        _fetched = true;

        if (!Shell.DryRun)
            Directory.CreateDirectory(Paths.SourceDir);

        if (localSource != null)
        {
            state.Source = new SourceState { Mode = "local" };

            foreach (var repository in Repositories)
            {
                var from = Path.Combine(localSource, repository);
                if (!Directory.Exists(from))
                    throw new InstallerException($"{from} does not exist");

                var to = Path.Combine(Paths.SourceDir, repository);
                InstallerLog.Write($"Copying {from} to {to}");

                if (Shell.DryRun)
                    continue;

                if (Directory.Exists(to))
                    Directory.Delete(to, true);

                CopyDirectory(from, to);
            }

            return;
        }

        if (state.Source.Mode == "local" && Directory.Exists(Path.Combine(Paths.SourceDir, "panel")))
        {
            InstallerLog.Write("Installed from a local checkout, keeping the sources (pass --source to replace them)");
            return;
        }

        EnsureGitSource(state);

        await CloneOrPullAsync(state.Source.PanelRepository, Path.Combine(Paths.SourceDir, "panel"), state.Source.PanelRef);
        await CloneOrPullAsync(state.Source.DemonRepository, Path.Combine(Paths.SourceDir, "demon"), state.Source.DemonRef);
    }

    /// <summary>Fills in the repositories for git installs, keeps the chosen channel and versions</summary>
    public static void EnsureGitSource(InstallState state)
    {
        if (state.Source.Mode == "git" && !string.IsNullOrEmpty(state.Source.PanelRepository))
            return;

        state.Source = new SourceState
        {
            Mode = "git",
            PanelRepository = Sources.PanelRepository,
            DemonRepository = Sources.DemonRepository,
            Branch = Sources.Branch,
            Channel = state.Source.Channel,
            PanelVersion = state.Source.PanelVersion,
            DemonVersion = state.Source.DemonVersion
        };
    }

    /// <param name="reference">Branch or release tag</param>
    private static async Task CloneOrPullAsync(string url, string target, string reference)
    {
        if (Directory.Exists(Path.Combine(target, ".git")))
        {
            // Works for branches and tags alike, the checkout always matches the remote exactly
            await Shell.RunAsync("git", "-C", target, "fetch", "--depth", "1", "--force", "origin", reference);
            await Shell.RunAsync("git", "-C", target, "reset", "--hard", "FETCH_HEAD");
            return;
        }

        if (Directory.Exists(target) && !Shell.DryRun)
            Directory.Delete(target, true);

        await Shell.RunAsync("git", "clone", "--depth", "1", "--branch", reference, "--", url, target);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(directory)))
                CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }
}
