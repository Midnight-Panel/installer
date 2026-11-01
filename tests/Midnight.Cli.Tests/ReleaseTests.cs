using Midnight.Cli.Core;

namespace Midnight.Cli.Tests;

public class ReleaseTests
{
    [Theory]
    [InlineData("https://github.com/Midnight-Panel/panel.git", "Midnight-Panel", "panel")]
    [InlineData("https://github.com/Midnight-Panel/demon", "Midnight-Panel", "demon")]
    [InlineData("git@github.com:someone/my.panel.git", "someone", "my.panel")]
    public void GitHubUrlsAreRecognised(string url, string owner, string repository)
        => Assert.Equal((owner, repository), Releases.ParseGitHub(url));

    [Theory]
    [InlineData("https://gitlab.com/Midnight-Panel/panel.git")]
    [InlineData("/srv/panel")]
    [InlineData("https://github.com/only-owner")]
    public void OtherSourcesHaveNoReleases(string url) => Assert.Null(Releases.ParseGitHub(url));

    [Fact]
    public void ReleasesAreNewestFirstWithoutDraftsAndUnsafeTags()
    {
        const string json = """
            [
              { "tag_name": "v1.0.0", "name": "First", "published_at": "2026-09-01T10:00:00Z", "prerelease": false, "draft": false },
              { "tag_name": "v1.2.0-beta", "name": "Beta", "published_at": "2026-10-20T10:00:00Z", "prerelease": true, "draft": false },
              { "tag_name": "v1.1.0", "name": "v1.1.0", "published_at": "2026-10-01T10:00:00Z", "prerelease": false, "draft": false },
              { "tag_name": "v2.0.0", "name": "Draft", "published_at": null, "prerelease": false, "draft": true },
              { "tag_name": "--upload-pack=x", "name": "evil", "published_at": "2026-10-30T10:00:00Z", "prerelease": false, "draft": false }
            ]
            """;

        var releases = Releases.Parse(json);

        Assert.Equal(["v1.2.0-beta", "v1.1.0", "v1.0.0"], releases.Select(r => r.Tag));
        Assert.True(releases[0].Prerelease);
    }

    [Fact]
    public void ReleaseChannelChecksOutTheTags()
    {
        var source = new SourceState { Branch = "main", Channel = SourceChannels.Release, PanelVersion = "v1.1.0", DemonVersion = "v1.0.0" };

        Assert.Equal("v1.1.0", source.PanelRef);
        Assert.Equal("v1.0.0", source.DemonRef);
        Assert.Equal("release v1.1.0", source.Describe(source.PanelVersion));
    }

    [Fact]
    public void CustomChannelAndOldStateFilesUseTheBranch()
    {
        // State files from before the version choice have no channel at all
        var state = System.Text.Json.JsonSerializer.Deserialize<InstallState>(
            """{ "source": { "mode": "git", "branch": "main", "panelVersion": "v9.9.9" } }""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Equal(SourceChannels.Custom, state.Source.Channel);
        Assert.Equal("main", state.Source.PanelRef);
        Assert.Equal("main", state.Source.DemonRef);
        Assert.StartsWith("custom", state.Source.Describe(null));
    }

    [Fact]
    public void ComputedRefsAreNotSaved()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new SourceState { Branch = "main" });

        Assert.DoesNotContain("PanelRef", json);
        Assert.DoesNotContain("DemonRef", json);
    }
}
