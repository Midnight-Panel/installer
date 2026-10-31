using System.Diagnostics;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Midnight.Cli.Core;

/// <summary>All console output and prompts of the installer and the cli</summary>
public static class Ui
{
    public const string Brand = "#465FFF";
    public const string Accent = "#A855F7";
    private const string Shadow = "#3B3F5C";

    /// <summary>Output is never wider than this, lines stay readable on wide terminals</summary>
    private static int Width => Math.Clamp(AnsiConsole.Profile.Width, 40, 78);

    /// <summary>False without a terminal (ci, cloud-init). Prompts then fail with a hint to the matching option.</summary>
    public static bool Interactive { get; set; } = !Console.IsInputRedirected;

    // ─── Layout ──────────────────────────────────────────────────────────────

    private static readonly string[] Logo =
    [
        "███╗   ███╗██╗██████╗ ███╗   ██╗██╗ ██████╗ ██╗  ██╗████████╗",
        "████╗ ████║██║██╔══██╗████╗  ██║██║██╔════╝ ██║  ██║╚══██╔══╝",
        "██╔████╔██║██║██║  ██║██╔██╗ ██║██║██║  ███╗███████║   ██║   ",
        "██║╚██╔╝██║██║██║  ██║██║╚██╗██║██║██║   ██║██╔══██║   ██║   ",
        "██║ ╚═╝ ██║██║██████╔╝██║ ╚████║██║╚██████╔╝██║  ██║   ██║   ",
        "╚═╝     ╚═╝╚═╝╚═════╝ ╚═╝  ╚═══╝╚═╝ ╚═════╝ ╚═╝  ╚═╝   ╚═╝   "
    ];

    /// <summary>Block logo with a blue to purple gradient, a compact one on narrow terminals</summary>
    public static void Banner(string subtitle)
    {
        AnsiConsole.WriteLine();

        if (AnsiConsole.Profile.Width >= Logo[0].Length + 4)
        {
            foreach (var line in Logo)
                AnsiConsole.MarkupLine("  " + Gradient(line));
        }
        else
        {
            AnsiConsole.MarkupLine($"  [bold {Brand}]◆ MIDNIGHT[/]");
        }

        AnsiConsole.WriteLine();
        var dryRun = Shell.DryRun ? "  [black on yellow] DRY RUN [/] [grey]nothing is changed[/]" : "";
        AnsiConsole.MarkupLine($"  [bold]Midnight Panel[/] [grey]· {Markup.Escape(subtitle)}[/]{dryRun}");
        AnsiConsole.WriteLine();
    }

    public static void Section(string title)
    {
        AnsiConsole.WriteLine();
        var label = $"[{Brand}]◆[/] [bold]{Markup.Escape(title)}[/] ";
        var rest = Math.Max(3, Width - 4 - title.Length - 3);
        AnsiConsole.MarkupLine($"  {label}[grey23]{new string('─', rest)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Success(string text) => AnsiConsole.MarkupLine($"  [green]✔[/] {text}");
    public static void Info(string text) => AnsiConsole.MarkupLine($"  [{Brand}]•[/] {text}");
    public static void Warning(string text) => AnsiConsole.MarkupLine($"  [yellow]▲[/] {text}");
    public static void Error(string text) => AnsiConsole.MarkupLine($"  [red]✖ {text}[/]");
    public static void Hint(string text) => AnsiConsole.MarkupLine($"    [grey]{text}[/]");

    /// <summary>Rounded box with a title, used for the system info, the summary and the result</summary>
    public static void Box(string title, IRenderable content, Color? border = null)
    {
        AnsiConsole.Write(new Padder(
            new Panel(content) { Width = Width - 2 }
                .Header($"\u2800[bold]{Markup.Escape(title)}[/]\u2800")
                .Border(BoxBorder.Rounded)
                .BorderColor(border ?? Color.Grey30)
                .Padding(2, 1, 2, 1),
            new Padding(2, 0, 0, 0)));
    }

    /// <summary>Aligned key/value rows, empty keys become spacer lines</summary>
    public static IRenderable KeyValues(IEnumerable<(string Key, string Value)> rows)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(3)).AddColumn();

        foreach (var (key, value) in rows)
            grid.AddRow(key.Length == 0 ? new Text("") : new Markup($"[grey]{Markup.Escape(key)}[/]"), new Markup(value));

        return grid;
    }

    /// <summary>Two column overview before installing</summary>
    public static void Summary(IEnumerable<(string Key, string Value)> rows) => Box("Summary", KeyValues(rows), Color.FromHex(Brand));

    // ─── Steps ───────────────────────────────────────────────────────────────

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
                    .Spinner(Spinner.Known.Dots12)
                    .SpinnerStyle(Style.Parse(Brand))
                    .StartAsync(Markup.Escape(title), async context =>
                    {
                        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                        var work = action();

                        while (!work.IsCompleted)
                        {
                            await Task.WhenAny(work, timer.WaitForNextTickAsync().AsTask());
                            context.Status($"{Markup.Escape(title)} [grey]{FormatDuration(stopwatch.Elapsed)}[/]");
                        }

                        await work;
                    });

            StepLine("[green]✔[/]", title, FormatDuration(stopwatch.Elapsed));
        }
        catch (Exception e)
        {
            StepLine("[red]✖[/]", title, "failed", "red");

            // The tail shows what ran before the error, the stack trace only goes to the log
            var tail = InstallerLog.Tail(15).Select(Markup.Escape).ToList();
            InstallerLog.Write($"FAILED: {title}: {e}");

            AnsiConsole.WriteLine();
            Box($"✖ {e.Message}", new Rows(
                new Markup($"[grey]{string.Join('\n', tail)}[/]"),
                new Text(""),
                new Markup($"[grey]Full log:[/] {Markup.Escape(InstallerLog.Path)}")), Color.Red);

            throw new InstallerAbortedException();
        }
    }

    /// <summary>"  ✔ Installing Docker ············ 12s"</summary>
    private static void StepLine(string icon, string title, string right, string rightStyle = "grey")
    {
        var dots = Math.Max(2, Width - 6 - title.Length - right.Length - 2);
        AnsiConsole.MarkupLine($"  {icon} {Markup.Escape(title)} [grey23]{new string('·', dots)}[/] [{rightStyle}]{right}[/]");
    }

    private static string FormatDuration(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s" : $"{elapsed.TotalSeconds:0}s";

    // ─── Prompts ─────────────────────────────────────────────────────────────

    // Spectre trims leading whitespace of prompts, the blank braille pattern is not whitespace and keeps the indent
    private static string Question(string question) => $"\u2800 [{Accent}]?[/] [bold]{question}[/]";

    public static string Ask(string question, string? defaultValue = null, Func<string, string?>? validate = null, string? option = null)
    {
        // Unattended: a default is a valid answer, only values without one have to be passed
        if (!Interactive && !string.IsNullOrEmpty(defaultValue) && validate?.Invoke(defaultValue) == null)
            return defaultValue;

        EnsureInteractive(question, option);

        var prompt = new TextPrompt<string>(Question(question)).PromptStyle(Brand);

        if (!string.IsNullOrEmpty(defaultValue))
            prompt.DefaultValue(defaultValue).DefaultValueStyle("grey");

        if (validate != null)
            prompt.Validate(value => validate(value.Trim()) is { } error ? ValidationResult.Error($"    [red]{error}[/]") : ValidationResult.Success());

        return AnsiConsole.Prompt(prompt).Trim();
    }

    public static string AskSecret(string question, Func<string, string?>? validate = null, string? option = null)
    {
        EnsureInteractive(question, option);

        var prompt = new TextPrompt<string>(Question(question)).Secret('•').PromptStyle(Brand);

        if (validate != null)
            prompt.Validate(value => validate(value) is { } error ? ValidationResult.Error($"    [red]{error}[/]") : ValidationResult.Success());

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

        return AnsiConsole.Prompt(new ConfirmationPrompt(Question(question)) { DefaultValue = defaultValue });
    }

    /// <param name="describe">Optional grey description shown next to each choice</param>
    public static T Choose<T>(string question, IEnumerable<T> choices, Func<T, string> display, Func<T, string?>? describe = null) where T : notnull
    {
        EnsureInteractive(question, null);

        var list = choices.ToList();
        var width = list.Max(x => display(x).Length);

        var choice = AnsiConsole.Prompt(new SelectionPrompt<T>()
            .Title(Question(question))
            .HighlightStyle(Style.Parse($"{Brand} bold"))
            .WrapAround()
            .AddChoices(list)
            .UseConverter(x => describe?.Invoke(x) is { } description
                ? $"{Markup.Escape(display(x).PadRight(width))}   [grey]{Markup.Escape(description)}[/]"
                : Markup.Escape(display(x))));

        // The selection prompt clears itself, keep the answer visible like the other prompts
        AnsiConsole.MarkupLine($"{Question(question)} [{Brand}]{Markup.Escape(display(choice))}[/]");
        return choice;
    }

    private static void EnsureInteractive(string question, string? option)
    {
        if (Interactive)
            return;

        throw new InstallerException(option == null
            ? $"'{question}' needs an interactive terminal"
            : $"Missing value for '{question}', pass {option}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Colors the blocks of a line from blue to purple, the shadow characters stay dark</summary>
    private static string Gradient(string line)
    {
        var from = Color.FromHex(Brand);
        var to = Color.FromHex(Accent);
        var builder = new StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == ' ')
            {
                builder.Append(' ');
                continue;
            }

            var color = c == '█' ? from.Blend(to, (float)i / (line.Length - 1)).ToHex() : Shadow.TrimStart('#');
            builder.Append($"[#{color}]{c}[/]");
        }

        return builder.ToString();
    }
}

/// <summary>Expected error with a message for the user, no stack trace</summary>
public class InstallerException(string message) : Exception(message);

/// <summary>A step failed and already printed its error</summary>
public class InstallerAbortedException() : Exception("Aborted");
