using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midnight.Cli.Core;

/// <summary>What is installed on this server and with which settings. Stored in /etc/midnight/midnight.json.</summary>
public class InstallState
{
    public string InstallerVersion { get; set; } = "";
    public PanelState? Panel { get; set; }
    public DaemonState? Daemon { get; set; }
    public SourceState Source { get; set; } = new();

    [JsonIgnore]
    public bool HasAnything => Panel != null || Daemon != null;

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

public class DaemonState
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
    public string DaemonRepository { get; set; } = "";
    public string Branch { get; set; } = "";
}

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
