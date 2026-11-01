using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midnight.Cli.Core;

/// <summary>What is installed on this server and with which settings. Stored in /etc/midnight/midnight.json.</summary>
public class InstallState
{
    public string InstallerVersion { get; set; } = "";
    public PanelState? Panel { get; set; }
    public DemonState? Demon { get; set; }
    public SourceState Source { get; set; } = new();

    /// <summary>Installations from before the rename stored the demon as "daemon", read only</summary>
    [JsonPropertyName("daemon"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DemonState? LegacyDaemon
    {
        get => null;
        set => Demon ??= value;
    }

    [JsonIgnore]
    public bool HasAnything => Panel != null || Demon != null;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static InstallState Load()
    {
        try
        {
            return File.Exists(Paths.StateFile)
                ? JsonSerializer.Deserialize<InstallState>(File.ReadAllText(Paths.StateFile), JsonOptions) ?? new InstallState()
                : new InstallState();
        }
        catch (Exception e) when (e is JsonException or UnauthorizedAccessException)
        {
            return new InstallState();
        }
    }

    public void Save()
    {
        if (Shell.DryRun)
            return;

        InstallerVersion = AppInfo.Version;
        Directory.CreateDirectory(Paths.EtcDir);
        File.WriteAllText(Paths.StateFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public class PanelState
{
    public bool Ssl { get; set; }
    public string Address { get; set; } = "";
    public int HttpPort { get; set; } = 80;
    public bool Registration { get; set; }
    public string AdminEmail { get; set; } = "";

    [JsonIgnore]
    public string Url => Ssl ? $"https://{Address}" : HttpPort == 80 ? $"http://{Address}" : $"http://{Address}:{HttpPort}";
}

public class DemonState
{
    public bool Ssl { get; set; }
    public string Address { get; set; } = "";
    public int Port { get; set; } = 8080;

    [JsonIgnore]
    public string Url => $"{(Ssl ? "https" : "http")}://{Address}:{Port}";
}

public class SourceState
{
    /// <summary>"git" or "local"</summary>
    public string Mode { get; set; } = "git";
    public string PanelRepository { get; set; } = "";
    public string DemonRepository { get; set; } = "";

    /// <summary>Name from before the rename, read only</summary>
    [JsonPropertyName("daemonRepository"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyDaemonRepository
    {
        get => null;
        set
        {
            if (string.IsNullOrEmpty(DemonRepository))
                DemonRepository = value ?? "";
        }
    }
    public string Branch { get; set; } = "";

    /// <summary>"custom" = newest code of Branch, "release" = the published versions below</summary>
    public string Channel { get; set; } = SourceChannels.Custom;

    /// <summary>Release tags, only used on the release channel</summary>
    public string? PanelVersion { get; set; }
    public string? DemonVersion { get; set; }

    /// <summary>Git ref of the panel sources (the demon builds against them too)</summary>
    [JsonIgnore]
    public string PanelRef => Channel == SourceChannels.Release && !string.IsNullOrEmpty(PanelVersion) ? PanelVersion : Branch;

    [JsonIgnore]
    public string DemonRef => Channel == SourceChannels.Release && !string.IsNullOrEmpty(DemonVersion) ? DemonVersion : Branch;

    public string Describe(string? version)
        => Channel == SourceChannels.Release && !string.IsNullOrEmpty(version) ? $"release {version}" : $"custom ({Branch}, newest code)";
}

public static class SourceChannels
{
    public const string Release = "release";
    public const string Custom = "custom";
}

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
