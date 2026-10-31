using System.Diagnostics;
using System.Text;

namespace Midnight.Cli.Core;

public class ShellOptions
{
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new();

    /// <summary>Don't throw on a non zero exit code</summary>
    public bool AllowFailure { get; init; }

    /// <summary>Connect stdin/stdout/stderr to the terminal (logs -f, editors). Output is not captured.</summary>
    public bool Interactive { get; init; }

    /// <summary>Values that must never end up in the log (passwords, tokens)</summary>
    public string[] Secrets { get; init; } = [];

    /// <summary>Written to stdin of the process</summary>
    public string? Input { get; init; }
}

public record ShellResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

public class ShellException(string command, ShellResult result)
    : Exception($"'{command}' failed with exit code {result.ExitCode}")
{
    public ShellResult Result { get; } = result;
}

/// <summary>
/// Runs external programs with an argument list (no shell parsing, so user input can't inject commands).
/// Everything is logged to the installer log. In dry run mode nothing is executed.
/// </summary>
public static class Shell
{
    public static bool DryRun { get; set; }

    public static async Task<ShellResult> RunAsync(string file, IEnumerable<string> arguments, ShellOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ShellOptions();
        var args = arguments.ToList();
        var display = Mask($"{file} {string.Join(' ', args.Select(Quote))}", options.Secrets);

        InstallerLog.Write($"$ {display}");

        if (DryRun)
            return new ShellResult(0, "");

        var startInfo = new ProcessStartInfo(file)
        {
            WorkingDirectory = options.WorkingDirectory ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = !options.Interactive,
            RedirectStandardError = !options.Interactive,
            RedirectStandardInput = options.Input != null
        };

        foreach (var argument in args)
            startInfo.ArgumentList.Add(argument);

        foreach (var (key, value) in options.Environment)
            startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();

        if (!options.Interactive)
        {
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);
        }

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            InstallerLog.Write($"  could not start {file}: {e.Message}");
            var missing = new ShellResult(127, e.Message);
            return options.AllowFailure ? missing : throw new ShellException(display, missing);
        }

        if (!options.Interactive)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        if (options.Input != null)
        {
            await process.StandardInput.WriteAsync(options.Input);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);

        var result = new ShellResult(process.ExitCode, output.ToString().TrimEnd());

        if (!result.Success)
            InstallerLog.Write($"  exit code {result.ExitCode}");

        if (!result.Success && !options.AllowFailure)
            throw new ShellException(display, result);

        return result;

        void Append(string? line)
        {
            if (line == null)
                return;

            lock (output)
                output.AppendLine(line);

            InstallerLog.Write("  " + Mask(line, options.Secrets));
        }
    }

    public static Task<ShellResult> RunAsync(string file, params string[] arguments) => RunAsync(file, arguments, null);

    /// <summary>True if the command exits with 0, never throws</summary>
    public static async Task<bool> SucceedsAsync(string file, params string[] arguments)
        => (await RunAsync(file, arguments, new ShellOptions { AllowFailure = true })).Success;

    public static bool CommandExists(string name)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, name)));

    internal static string Mask(string text, IEnumerable<string> secrets)
    {
        foreach (var secret in secrets.Where(x => !string.IsNullOrEmpty(x)))
            text = text.Replace(secret, "***");

        return text;
    }

    private static string Quote(string argument)
        => argument.Length == 0 || argument.Any(char.IsWhiteSpace) || argument.Contains('"') ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;
}
