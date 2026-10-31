namespace Midnight.Cli.Core;

/// <summary>Detailed log of everything the installer runs. Shown partly when a step fails.</summary>
public static class InstallerLog
{
    private static readonly Lock Sync = new();
    private static string? _path;

    public static string Path => _path ?? Paths.LogFile;

    public static void Initialize(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            _path = path;
        }
        catch (Exception)
        {
            // Not root (e.g. "midnight status"), log next to the temp files instead
            _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "midnight-cli.log");
        }
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static string[] Tail(int lines)
    {
        try
        {
            var all = File.ReadAllLines(Path);
            return all.Skip(Math.Max(0, all.Length - lines)).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
