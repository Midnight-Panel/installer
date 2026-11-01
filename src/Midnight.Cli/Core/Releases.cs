using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midnight.Cli.Core;

public record ReleaseInfo(string Tag, string? Name, DateTimeOffset? PublishedAt, bool Prerelease);

/// <summary>Published versions of the panel and demon repositories (GitHub releases)</summary>
public static partial class Releases
{
    /// <summary>"https://github.com/owner/repo(.git)" or "git@github.com:owner/repo.git", null for other hosts</summary>
    public static (string Owner, string Repository)? ParseGitHub(string url)
    {
        var match = GitHubUrl().Match(url.Trim());
        return match.Success ? (match.Groups["owner"].Value, match.Groups["repo"].Value) : null;
    }

    /// <summary>Newest first, drafts left out. Null when the repository is not on GitHub or GitHub could not be reached.</summary>
    public static async Task<List<ReleaseInfo>?> FetchAsync(string repositoryUrl)
    {
        if (ParseGitHub(repositoryUrl) is not { } repository)
            return null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"midnight-installer/{AppInfo.Version}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            // Optional, raises the GitHub rate limit of 60 requests per hour
            if (Environment.GetEnvironmentVariable("MIDNIGHT_GITHUB_TOKEN") is { Length: > 0 } token)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var url = $"https://api.github.com/repos/{repository.Owner}/{repository.Repository}/releases?per_page=30";
            InstallerLog.Write($"GET {url}");

            return Parse(await client.GetStringAsync(url));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            InstallerLog.Write($"Could not load the releases of {repositoryUrl}: {e.Message}");
            return null;
        }
    }

    internal static List<ReleaseInfo> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;

            var tag = release.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag) || !SafeTag().IsMatch(tag))
                continue;

            releases.Add(new ReleaseInfo(
                tag,
                release.TryGetProperty("name", out var name) ? name.GetString() : null,
                release.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String
                    ? published.GetDateTimeOffset()
                    : null,
                release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()));
        }

        return releases.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue).ToList();
    }

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:)(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9._-]+?)(?:\.git)?/?$")]
    private static partial Regex GitHubUrl();

    /// <summary>Tags end up in git commands, only plain version names are accepted</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}\z")]
    private static partial Regex SafeTag();
}
