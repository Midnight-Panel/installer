namespace Midnight.Cli.Core;

/// <summary>Fixed locations of a Midnight installation</summary>
public static class Paths
{
    public const string BaseDir = "/opt/midnight";
    public const string SourceDir = BaseDir + "/src";
    public const string PanelDir = BaseDir + "/panel";

    public const string EtcDir = "/etc/midnight";
    public const string StateFile = EtcDir + "/midnight.json";
    public const string DaemonConfig = EtcDir + "/config.json";

    public const string DaemonBinary = "/usr/local/bin/midnight-daemon";
    public const string DaemonUnit = "/etc/systemd/system/midnight-daemon.service";
    public const string DaemonService = "midnight-daemon";
    public const string DaemonData = "/var/lib/midnight";

    public const string CliBinary = "/usr/local/bin/midnight";
    public const string LogFile = "/var/log/midnight-installer.log";
    public const string BackupDir = "/var/backups/midnight";

    /// <summary>Docker network of the panel stack, the panel trusts X-Forwarded-* headers from it</summary>
    public const string PanelSubnet = "172.30.0.0/16";
    public const string PanelNetwork = "midnight_midnight";

    public static string CertificateDir(string domain) => $"/etc/letsencrypt/live/{domain}";
}

/// <summary>Where sources and the cli itself come from. Overridable with environment variables.</summary>
public static class Sources
{
    public static string PanelRepository => Env("MIDNIGHT_PANEL_REPO", "https://github.com/Midnight-Panel/panel.git");
    public static string DaemonRepository => Env("MIDNIGHT_DAEMON_REPO", "https://github.com/Midnight-Panel/demon.git");
    public static string Branch => Env("MIDNIGHT_BRANCH", "main");

    /// <summary>Base url of the cli release binaries (midnight-linux-x64, midnight-linux-arm64)</summary>
    public static string CliReleaseUrl => Env("MIDNIGHT_CLI_URL", "https://github.com/Midnight-Panel/installer/releases/latest/download");

    private static string Env(string name, string fallback)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
