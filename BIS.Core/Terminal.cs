using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BIS.Core;

public static class Terminal
{
    // ── Colour scheme ──────────────────────────────────────────────
    private const string Heading = "steelblue1";
    private const string Primary = "deepskyblue2";
    private const string ColSuccess = "green";
    private const string ColWarning = "orange1";
    private const string ColError = "red";
    private const string ColMuted = "grey";

    // ── Top-level helpers ──────────────────────────────────────────

    public static void Banner(string text)
    {
        AnsiConsole.Write(new FigletText(text).Color(Color.SteelBlue).Centered());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Rounded panel with a heading — the main "card" for command output.
    /// </summary>
    public static void Card(string title, Action<Table> contentBuilder)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(""));
        contentBuilder(table);
        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[bold {Heading}]{MarkupEncode(title)}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1, 2, 1),
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void Card(string title, params string[] markupLines)
    {
        Card(title, t =>
        {
            foreach (var line in markupLines)
                t.AddRow(new Markup(line));
        });
    }

    public static void SuccessCard(string title, string detail = null)
    {
        if (detail != null)
            Card(title, t => t.AddRow(new Markup($"[{ColSuccess}]{MarkupEncode(detail)}[/]")));
        else
            Card(title, "");
    }

    public static void ErrorCard(string title, string detail = null)
    {
        var content = detail != null
            ? new Markup($"[{ColError}]{MarkupEncode(detail)}[/]")
            : new Markup("");
        var panel = new Panel(content)
        {
            Header = new PanelHeader($"[bold {ColError}]{MarkupEncode(title)}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1, 2, 1),
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    // ── Section separator ──────────────────────────────────────────

    public static void Section(string title)
    {
        AnsiConsole.Write(new Rule($"[dim {ColMuted}]{MarkupEncode(title)}[/]") { Justification = Justify.Left });
        AnsiConsole.WriteLine();
    }

    // ── Coloured text helpers ──────────────────────────────────────

    public static void Info(string message) => WriteLine(Primary, message);
    public static void Success(string message) => WriteLine(ColSuccess, message);
    public static void Warning(string message) => WriteLine(ColWarning, message);
    public static void Error(string message) => WriteLine(ColError, message);
    public static void Muted(string message) => WriteLine(ColMuted, message);

    public static void WriteInfo(string message) => Write(Primary, message);
    public static void WriteSuccess(string message) => Write(ColSuccess, message);

    public static void Entry(string label, object value, string valueColor = null)
    {
        var vc = valueColor ?? "white";
        AnsiConsole.MarkupLine($"  [bold {ColMuted}]{MarkupEncode(label)}:[/]  [{vc}]{MarkupEncode(value.ToString())}[/]");
    }

    public static void Row(string label, object value, string valueColor = null)
    {
        var vc = valueColor ?? "white";
        AnsiConsole.Markup($"  [bold {ColMuted}]{MarkupEncode(label)}[/]");
        AnsiConsole.MarkupLine($"  [{vc}]{MarkupEncode(value.ToString())}[/]");
    }

    public static void FileLine(string name, long bytes, string suffix = null)
    {
        var size = FormatBytes(bytes);
        AnsiConsole.MarkupLine($"  {MarkupEncode(name),-40} [dim]{size,8}[/]{suffix}");
    }

    public static void FileLineRecovered(string name, long bytes, string originalName = null)
    {
        var size = FormatBytes(bytes);
        AnsiConsole.MarkupLine($"  {MarkupEncode(name),-40} [dim]{size,8}[/] [green]✔[/]");
        if (originalName != null)
            AnsiConsole.MarkupLine($"    [dim](was: {MarkupEncode(originalName)})[/]");
    }

    public static void Table(string[] columns, IEnumerable<string[]> rows)
    {
        var table = new Table().Border(TableBorder.Square);
        foreach (var col in columns)
            table.AddColumn(new TableColumn($"[bold]{MarkupEncode(col)}[/]"));
        foreach (var row in rows)
            table.AddRow(row.Select(c => new Markup(c)).ToArray());
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    // ── Progress ───────────────────────────────────────────────────

    public static void Progress(string taskLabel, int maxValue, Action<ProgressTask> work)
    {
        AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .Start(ctx =>
            {
                var task = ctx.AddTask(taskLabel, new ProgressTaskSettings { MaxValue = maxValue });
                work(task);
                task.Value = maxValue;
            });
    }

    public static void Status(string statusMessage, Action action)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Primary))
            .Start(statusMessage, _ => action());
    }

    public static T Status<T>(string statusMessage, Func<T> func)
    {
        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Primary))
            .Start(statusMessage, _ => func());
    }

    // ── Internal helpers ───────────────────────────────────────────

    private static void WriteLine(string color, string text)
    {
        AnsiConsole.MarkupLine($"[{color}]{MarkupEncode(text)}[/]");
    }

    private static void Write(string color, string text)
    {
        AnsiConsole.Markup($"[{color}]{MarkupEncode(text)}[/]");
    }

    public static string MarkupEncode(string text)
    {
        return (text ?? "").Replace("[", "[[").Replace("]", "]]");
    }

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public static string FormatCount(int count) => count.ToString("N0");
}
