using System.Runtime.InteropServices;

namespace Midnight.Cli.Core;

/// <summary>Puts the running binary to /usr/local/bin/midnight and updates it from the releases</summary>
public static class SelfInstall
{
    public static async Task InstallAsync()
    {
        var current = Environment.ProcessPath;

        if (current == null || current == Paths.CliBinary || Shell.DryRun)
            return;

        // "dotnet midnight.dll" during development has no single binary to copy
        if (Path.GetFileNameWithoutExtension(current) != "midnight")
        {
            InstallerLog.Write($"Not installing {current} as cli, it is no published midnight binary");
            return;
        }

        await ReplaceAsync(target => { File.Copy(current, target, true); return Task.CompletedTask; });
    }

    /// <summary>Downloads the newest release for this architecture</summary>
    public static async Task<string> UpdateAsync()
    {
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var url = $"{Sources.CliReleaseUrl}/midnight-linux-{architecture}";

        if (Shell.DryRun)
            return url;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        await ReplaceAsync(async target =>
        {
            await using var download = await http.GetStreamAsync(url);
            await using var file = File.Create(target);
            await download.CopyToAsync(file);
        });

        return url;
    }

    /// <summary>Writes to a temp file next to the binary and renames it, so a running cli is never half written</summary>
    private static async Task ReplaceAsync(Func<string, Task> write)
    {
        var temp = Paths.CliBinary + ".new";

        await write(temp);
        File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(temp, Paths.CliBinary, true);

        InstallerLog.Write($"Installed cli to {Paths.CliBinary}");
    }
}
