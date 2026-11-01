namespace Midnight.Cli.Core;

/// <summary>Fixed locations of a Midnight installation</summary>
public static class Paths
{
    public const string BaseDir = "/opt/midnight";
    public const string SourceDir = BaseDir + "/src";
    public const string PanelDir = BaseDir + "/panel";

    /// <summary>Maintenance page for the proxy; while the flag file exists every request gets the page (503)</summary>
    public const string MaintenanceDir = PanelDir + "/maintenance";
    public const string MaintenanceFlag = MaintenanceDir + "/enabled";

    public const string EtcDir = "/etc/midnight";
    public const string StateFile = EtcDir + "/midnight.json";
    public const string DemonConfig = EtcDir + "/config.json";

    public const string DemonBinary = "/usr/local/bin/midnight-demon";
    public const string DemonUnit = "/etc/systemd/system/midnight-demon.service";
    public const string DemonService = "midnight-demon";
    public const string DemonData = "/var/lib/midnight";

    /// <summary>Names from before the demon was renamed, migrated by the installer</summary>
    public const string LegacyDemonService = "midnight-daemon";
    public const string LegacyDemonUnit = "/etc/systemd/system/midnight-daemon.service";
    public const string LegacyDemonBinary = "/usr/local/bin/midnight-daemon";
    public const string CertbotRenewalDir = "/etc/letsencrypt/renewal";

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
    public static string DemonRepository => Env("MIDNIGHT_DEMON_REPO", "https://github.com/Midnight-Panel/demon.git");
    public static string Branch => Env("MIDNIGHT_BRANCH", "main");

    /// <summary>Base url of the cli release binaries (midnight-linux-x64, midnight-linux-arm64)</summary>
    public static string CliReleaseUrl => Env("MIDNIGHT_CLI_URL", "https://github.com/Midnight-Panel/installer/releases/latest/download");

    private static string Env(string name, string fallback)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
