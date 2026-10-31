using System.Diagnostics;
using Spectre.Console;

namespace Midnight.Cli.Core;

/// <summary>All console output and prompts of the installer and the cli</summary>
public static class Ui
{
    public const string Brand = "#465FFF";
    public const string Accent = "mediumpurple1";

    /// <summary>False without a terminal (ci, cloud-init). Prompts then fail with a hint to the matching option.</summary>
    public static bool Interactive { get; set; } = !Console.IsInputRedirected;

    public static void Banner(string subtitle)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText("Midnight").Color(Color.FromHex(Brand)));
        AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(subtitle)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Section(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(title)}[/]").LeftJustified().RuleStyle(Style.Parse(Brand)));
        AnsiConsole.WriteLine();
    }

    public static void Success(string text) => AnsiConsole.MarkupLine($"  [green]✔[/] {text}");
    public static void Info(string text) => AnsiConsole.MarkupLine($"  [{Brand}]•[/] {text}");
    public static void Warning(string text) => AnsiConsole.MarkupLine($"  [yellow]![/] {text}");
    public static void Error(string text) => AnsiConsole.MarkupLine($"  [red]✖ {text}[/]");
    public static void Hint(string text) => AnsiConsole.MarkupLine($"    [grey]{text}[/]");

    /// <summary>Runs an installation step with spinner and elapsed time. On failure the end of the log is shown.</summary>
    public static async Task StepAsync(string title, Func<Task> action)
    {
        InstallerLog.Write($"STEP: {title}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Without a terminal (piped, CI) a spinner only adds noise
            if (!AnsiConsole.Profile.Capabilities.Interactive)
                await action();
            else
                await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots2)
                .SpinnerStyle(Style.Parse(Brand))
                .StartAsync(Markup.Escape(title), async context =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                    var work = action();

                    while (!work.IsCompleted)
                    {
                        await Task.WhenAny(work, timer.WaitForNextTickAsync().AsTask());
                        context.Status($"{Markup.Escape(title)} [grey]({stopwatch.Elapsed.TotalSeconds:0}s)[/]");
                    }

                    await work;
                });

            AnsiConsole.MarkupLine($"  [green]✔[/] {Markup.Escape(title)} [grey]({stopwatch.Elapsed.TotalSeconds:0}s)[/]");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"  [red]✖[/] {Markup.Escape(title)}");

            // The tail shows what ran before the error, the stack trace only goes to the log
            var tail = string.Join('\n', InstallerLog.Tail(20).Select(Markup.Escape));
            InstallerLog.Write($"FAILED: {title}: {e}");
            AnsiConsole.Write(new Panel(new Markup($"[grey]{tail}[/]"))
                .Header($"[red] {Markup.Escape(e.Message)} [/]")
                .BorderColor(Color.Red)
                .Expand());
            AnsiConsole.MarkupLine($"  [grey]Full log: {Markup.Escape(InstallerLog.Path)}[/]");

            throw new InstallerAbortedException();
        }
    }

    public static string Ask(string question, string? defaultValue = null, Func<string, string?>? validate = null, string? option = null)
    {
        // Unattended: a default is a valid answer, only values without one have to be passed
        if (!Interactive && !string.IsNullOrEmpty(defaultValue) && validate?.Invoke(defaultValue) == null)
            return defaultValue;

        EnsureInteractive(question, option);

        var prompt = new TextPrompt<string>($"[{Accent}]?[/] [bold]{question}[/]").PromptStyle(Brand);

        if (!string.IsNullOrEmpty(defaultValue))
            prompt.DefaultValue(defaultValue).DefaultValueStyle("grey");

        if (validate != null)
            prompt.Validate(value => validate(value.Trim()) is { } error ? ValidationResult.Error($"  [red]{error}[/]") : ValidationResult.Success());

        return AnsiConsole.Prompt(prompt).Trim();
    }

    public static string AskSecret(string question, Func<string, string?>? validate = null, string? option = null)
    {
        EnsureInteractive(question, option);

        var prompt = new TextPrompt<string>($"[{Accent}]?[/] [bold]{question}[/]").Secret();

        if (validate != null)
            prompt.Validate(value => validate(value) is { } error ? ValidationResult.Error($"  [red]{error}[/]") : ValidationResult.Success());

        return AnsiConsole.Prompt(prompt);
    }

    public static int AskPort(string question, int defaultValue, string? option = null)
    {
        while (true)
        {
            var value = Ask(question, defaultValue.ToString(), v => int.TryParse(v, out var port) && Validation.IsPort(port) ? null : "Enter a port between 1 and 65535", option);
            var port = int.Parse(value);

            if (!HostInfo.IsPortInUse(port) || Confirm($"Port {port} is already in use. Use it anyway?", false))
                return port;
        }
    }

    public static bool Confirm(string question, bool defaultValue = true)
    {
        if (!Interactive)
            return defaultValue;

        return AnsiConsole.Prompt(new ConfirmationPrompt($"[{Accent}]?[/] [bold]{question}[/]") { DefaultValue = defaultValue });
    }

    public static T Choose<T>(string question, IEnumerable<T> choices, Func<T, string> display) where T : notnull
    {
        EnsureInteractive(question, null);

        return AnsiConsole.Prompt(new SelectionPrompt<T>()
            .Title($"[{Accent}]?[/] [bold]{question}[/]")
            .HighlightStyle(Style.Parse($"{Brand} bold"))
            .AddChoices(choices)
            .UseConverter(display));
    }

    /// <summary>Two column overview, e.g. the summary before installing</summary>
    public static void Summary(IEnumerable<(string Key, string Value)> rows)
    {
        var grid = new Grid().AddColumn(new GridColumn().PadLeft(4).PadRight(3)).AddColumn();

        foreach (var (key, value) in rows)
            grid.AddRow($"[grey]{Markup.Escape(key)}[/]", value);

        AnsiConsole.Write(grid);
    }

    private static void EnsureInteractive(string question, string? option)
    {
        if (Interactive)
            return;

        throw new InstallerException(option == null
            ? $"'{question}' needs an interactive terminal"
            : $"Missing value for '{question}', pass {option}");
    }
}

/// <summary>Expected error with a message for the user, no stack trace</summary>
public class InstallerException(string message) : Exception(message);

/// <summary>A step failed and already printed its error</summary>
public class InstallerAbortedException() : Exception("Aborted");
