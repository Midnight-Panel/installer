using Midnight.Cli.Core;
using Spectre.Console;

namespace Midnight.Cli.Components;

/// <summary>Release or custom (newest code) and which release – for installs and updates</summary>
public static class VersionSelector
{
    /// <param name="channel">--channel, null = ask (unattended: release when there is one)</param>
    public static async Task SelectForInstallAsync(InstallState state, bool panel, bool demon, string? channel, string? panelVersion, string? demonVersion)
    {
        Ui.Section("Version");
        SourceManager.EnsureGitSource(state);
        var source = state.Source;

        // The demon builds against the panel's shared code, so the panel releases are needed in both cases
        var panelReleases = await LoadAsync(source.PanelRepository);
        var demonReleases = demon ? await LoadAsync(source.DemonRepository) : [];
        var available = panelReleases.Count > 0 && (!demon || demonReleases.Count > 0);

        channel = channel?.ToLowerInvariant() ?? (panelVersion ?? demonVersion) switch
        {
            not null => SourceChannels.Release,
            null when !Ui.Interactive => available ? SourceChannels.Release : SourceChannels.Custom,
            _ => AskChannel(available, source.Branch)
        };

        if (channel is not (SourceChannels.Release or SourceChannels.Custom))
            throw new InstallerException($"Unknown channel '{channel}', use release or custom");

        if (channel == SourceChannels.Release && !available)
        {
            Ui.Warning($"No releases published yet, installing the newest code of {Markup.Escape(source.Branch)} instead");
            channel = SourceChannels.Custom;
        }

        source.Channel = channel;
        source.PanelVersion = null;
        source.DemonVersion = null;

        if (channel == SourceChannels.Custom)
        {
            Ui.Info($"Installing the newest code of [bold]{Markup.Escape(source.Branch)}[/]");
            return;
        }

        if (panel)
            source.PanelVersion = Pick("Panel version", panelReleases, panelVersion, "--panel-version");

        if (demon)
            source.DemonVersion = Pick("Demon version", demonReleases, demonVersion, "--demon-version");

        if (demon && !panel)
            source.PanelVersion = MatchingPanelVersion(source.DemonVersion!, panelReleases);
    }

    /// <summary>On the release channel: choose one of the newer releases (unattended: the newest)</summary>
    public static async Task SelectForUpdateAsync(InstallState state, bool panel, bool demon, bool latest)
    {
        var source = state.Source;

        if (source.Mode != "git" || source.Channel != SourceChannels.Release)
            return;

        Ui.Section("Version");
        var panelReleases = await LoadAsync(source.PanelRepository);

        if (panel)
            source.PanelVersion = PickUpdate("Panel", panelReleases, source.PanelVersion, latest);

        if (demon)
        {
            var demonReleases = await LoadAsync(source.DemonRepository);
            source.DemonVersion = PickUpdate("Demon", demonReleases, source.DemonVersion, latest);

            if (!panel)
                source.PanelVersion = MatchingPanelVersion(source.DemonVersion!, panelReleases);
        }
    }

    private static string AskChannel(bool releasesAvailable, string branch)
    {
        var options = new List<(string Key, string Label, string Description)>
        {
            (SourceChannels.Release, "Release", releasesAvailable ? "a published version, recommended" : "no releases published yet"),
            (SourceChannels.Custom, "Custom", $"newest code of {branch}, may contain unfinished work")
        };

        return Ui.Choose("Which version do you want to install?", options, x => x.Label, x => x.Description).Key;
    }

    private static string Pick(string label, List<ReleaseInfo> releases, string? given, string option)
    {
        if (given != null)
        {
            if (releases.All(r => r.Tag != given))
                throw new InstallerException($"{label} '{given}' does not exist. Published: {string.Join(", ", releases.Take(10).Select(r => r.Tag))}");

            return given;
        }

        var newest = releases.FirstOrDefault(r => !r.Prerelease) ?? releases[0];

        if (!Ui.Interactive)
            return newest.Tag;

        // The newest stable release first, pre-releases stay selectable
        var ordered = releases.OrderBy(r => r == newest ? 0 : 1).ToList();
        return Ui.Choose(label, ordered, r => r.Tag, r => Describe(r, r == newest)).Tag;
    }

    private static string PickUpdate(string label, List<ReleaseInfo> releases, string? current, bool latest)
    {
        if (releases.Count == 0)
        {
            Ui.Warning($"Could not load the {label.ToLowerInvariant()} releases, keeping {Markup.Escape(current ?? "the installed version")}");
            return current ?? throw new InstallerException($"No {label.ToLowerInvariant()} releases found");
        }

        // Releases are sorted newest first, everything before the installed one is newer
        var index = current == null ? -1 : releases.FindIndex(r => r.Tag == current);
        var newer = (index < 0 ? releases : releases.Take(index)).ToList();

        if (newer.Count == 0)
        {
            Ui.Success($"{label} is on the newest release ({Markup.Escape(current!)})");
            return current!;
        }

        var target = newer.FirstOrDefault(r => !r.Prerelease) ?? newer[0];

        if (latest || !Ui.Interactive)
        {
            Ui.Info($"{label}: {Markup.Escape(current ?? "?")} → [bold]{Markup.Escape(target.Tag)}[/]");
            return target.Tag;
        }

        var choices = newer.OrderBy(r => r == target ? 0 : 1).Select(r => (Release: (ReleaseInfo?)r, Keep: false)).ToList();
        if (current != null)
            choices.Add((null, true));

        var choice = Ui.Choose(
            $"Update the {label.ToLowerInvariant()} to",
            choices,
            c => c.Keep ? $"Keep {current}" : c.Release!.Tag,
            c => c.Keep ? "no update" : Describe(c.Release!, c.Release == target));

        return choice.Keep ? current! : choice.Release!.Tag;
    }

    /// <summary>The demon needs panel sources of the same version, otherwise the newest panel release</summary>
    private static string MatchingPanelVersion(string demonVersion, List<ReleaseInfo> panelReleases)
    {
        if (panelReleases.Any(r => r.Tag == demonVersion))
            return demonVersion;

        var fallback = panelReleases.FirstOrDefault(r => !r.Prerelease) ?? panelReleases.FirstOrDefault()
                       ?? throw new InstallerException("The demon needs the panel sources, but the panel has no releases");

        Ui.Warning($"The panel has no release {Markup.Escape(demonVersion)}, the demon is built against panel {Markup.Escape(fallback.Tag)}");
        return fallback.Tag;
    }

    private static string Describe(ReleaseInfo release, bool recommended)
    {
        var parts = new List<string>();

        if (recommended) parts.Add("newest");
        if (release.Prerelease) parts.Add("pre-release");
        if (release.PublishedAt is { } published) parts.Add(published.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(release.Name) && release.Name != release.Tag) parts.Add(release.Name);

        return string.Join(" · ", parts);
    }

    private static async Task<List<ReleaseInfo>> LoadAsync(string repository)
    {
        List<ReleaseInfo>? releases = null;
        await Ui.StepAsync($"Loading releases of {Releases.ParseGitHub(repository)?.Repository ?? repository}", async () => releases = await Releases.FetchAsync(repository));
        return releases ?? [];
    }
}
