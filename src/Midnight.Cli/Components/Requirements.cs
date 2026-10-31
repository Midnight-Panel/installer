using Midnight.Cli.Core;

namespace Midnight.Cli.Components;

/// <summary>System packages both components need</summary>
public static class Requirements
{
    // "install all" runs panel and daemon setup in one process, each requirement is only needed once
    private static bool _basePackages, _docker;

    public static async Task InstallBasePackagesAsync(HostInfo host)
    {
        if (_basePackages)
            return;

        _basePackages = true;
        if (host.PackageManager == "apt")
        {
            var environment = new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };
            await Shell.RunAsync("apt-get", ["update", "-y"], new ShellOptions { Environment = environment });
            await Shell.RunAsync("apt-get", ["install", "-y", "--no-install-recommends", "curl", "ca-certificates", "git", "openssl", "tar"], new ShellOptions { Environment = environment });
        }
        else
        {
            await Shell.RunAsync("dnf", "install", "-y", "curl", "ca-certificates", "git", "openssl", "tar");
        }
    }

    public static async Task<bool> IsDockerReadyAsync()
        => Shell.CommandExists("docker") && await Shell.SucceedsAsync("docker", "compose", "version");

    /// <summary>Installs docker engine and the compose plugin with Docker's official script</summary>
    public static async Task InstallDockerAsync()
    {
        if (_docker)
            return;

        _docker = true;
        if (!await IsDockerReadyAsync())
        {
            // Private temp directory (0700): a fixed name in /tmp could be prepared by another user and run as root
            var directory = Shell.DryRun ? Path.GetTempPath() : Directory.CreateTempSubdirectory("midnight-").FullName;
            var script = Path.Combine(directory, "get-docker.sh");

            try
            {
                if (!Shell.DryRun)
                {
                    using var http = new HttpClient();
                    await File.WriteAllTextAsync(script, await http.GetStringAsync("https://get.docker.com"));
                }

                await Shell.RunAsync("sh", script);
            }
            finally
            {
                if (!Shell.DryRun)
                    Directory.Delete(directory, true);
            }
        }

        await Shell.RunAsync("systemctl", "enable", "--now", "docker");
    }

    public static async Task InstallCertbotAsync(HostInfo host)
    {
        if (Shell.CommandExists("certbot"))
            return;

        if (host.PackageManager == "apt")
            await Shell.RunAsync("apt-get", ["install", "-y", "certbot"], new ShellOptions { Environment = new() { ["DEBIAN_FRONTEND"] = "noninteractive" } });
        else
        {
            await Shell.RunAsync("dnf", "install", "-y", "certbot");

            // Unlike the Debian package the renewal timer of the rpm is not enabled by default
            await Shell.RunAsync("systemctl", ["enable", "--now", "certbot-renew.timer"], new ShellOptions { AllowFailure = true });
        }
    }
}
