using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Midnight.Cli.Core;

/// <summary>Facts about the server the installer runs on</summary>
public class HostInfo
{
    public string OsName { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string PackageManager { get; init; } = "";
    public double MemoryGb { get; init; }
    public string PublicIp { get; init; } = "";

    /// <summary>.NET runtime identifier suffix: x64 or arm64</summary>
    public string RuntimeArchitecture => Architecture == "arm64" ? "arm64" : "x64";

    public static bool IsRoot => Environment.UserName == "root" || (OperatingSystem.IsLinux() && geteuid() == 0);

    [DllImport("libc")]
    private static extern uint geteuid();

    public static async Task<HostInfo> DetectAsync()
    {
        var osRelease = ReadKeyValues("/etc/os-release");
        var memoryKb = ReadKeyValues("/proc/meminfo", ':').TryGetValue("MemTotal", out var mem)
            ? long.Parse(mem.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            : 0;

        return new HostInfo
        {
            OsName = osRelease.GetValueOrDefault("PRETTY_NAME") ?? RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64",
            PackageManager = Shell.CommandExists("apt-get") ? "apt" : Shell.CommandExists("dnf") ? "dnf" : "",
            MemoryGb = memoryKb / 1024d / 1024d,
            PublicIp = await GetPublicIpAsync()
        };
    }

    public void EnsureSupported()
    {
        if (!OperatingSystem.IsLinux())
            throw new InstallerException("Midnight can only be installed on Linux (use --dry-run to try the installer elsewhere)");

        if (!IsRoot)
            throw new InstallerException("Run the installer as root: sudo midnight install");

        if (!Shell.CommandExists("systemctl"))
            throw new InstallerException("systemd is required");

        if (PackageManager == "")
            throw new InstallerException($"Unsupported distribution {OsName} (apt or dnf required)");
    }

    public static bool IsPortInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public static async Task<string?> ResolveAsync(string host)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork);
            return addresses.FirstOrDefault()?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string> GetPublicIpAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var url in new[] { "https://api.ipify.org", "https://ifconfig.me/ip" })
        {
            try
            {
                var ip = (await http.GetStringAsync(url)).Trim();
                if (Validation.IsIpv4(ip))
                    return ip;
            }
            catch (Exception)
            {
                // Try the next service
            }
        }

        // Offline: first non loopback address
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Select(x => x.Address)
            .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?
            .ToString() ?? "127.0.0.1";
    }

    private static Dictionary<string, string> ReadKeyValues(string path, char separator = '=')
    {
        var result = new Dictionary<string, string>();

        if (!File.Exists(path))
            return result;

        foreach (var line in File.ReadAllLines(path))
        {
            var index = line.IndexOf(separator);
            if (index > 0)
                result[line[..index].Trim()] = line[(index + 1)..].Trim().Trim('"');
        }

        return result;
    }
}
