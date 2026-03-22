using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace MsGraphCli.Output;

/// <summary>
/// Determines how results are written to the user.
/// </summary>
public interface IOutputFormatter
{
    void WriteResult<T>(T data, TextWriter stdout);
    void WriteError(string errorCode, string message, TextWriter stderr);
    void WriteMessage(string message, TextWriter stderr);
}

/// <summary>
/// Writes results as formatted JSON to stdout.
/// </summary>
public sealed class JsonOutputFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public void WriteResult<T>(T data, TextWriter stdout)
    {
        string json = JsonSerializer.Serialize(data, Options);
        stdout.WriteLine(json);
    }

    public void WriteError(string errorCode, string message, TextWriter stderr)
    {
        var error = new { error = new { code = errorCode, message } };
        string json = JsonSerializer.Serialize(error, Options);
        stderr.WriteLine(json);
    }

    public void WriteMessage(string message, TextWriter stderr)
    {
        // In JSON mode, diagnostic messages are still plain text on stderr
        stderr.WriteLine(message);
    }
}

/// <summary>
/// Writes results as human-friendly tables using Spectre.Console.
/// </summary>
public sealed class TableOutputFormatter : IOutputFormatter
{
    public void WriteResult<T>(T data, TextWriter stdout)
    {
        // For table mode, each command is responsible for calling
        // the appropriate WriteTable* method. This generic fallback
        // serializes to JSON as a safety net.
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        stdout.WriteLine(json);
    }

    public void WriteError(string errorCode, string message, TextWriter stderr)
    {
        AnsiConsole.MarkupLine($"[red]Error[/] ({errorCode}): {Markup.Escape(message)}");
    }

    public void WriteMessage(string message, TextWriter stderr)
    {
        stderr.WriteLine(message);
    }

    // ── Typed table writers for specific data types ──

    public static void WriteMailTable(IReadOnlyList<MsGraphCli.Core.Models.MailMessageSummary> messages)
    {
        var table = new Table();
        table.AddColumn("FROM");
        table.AddColumn("SUBJECT");
        table.AddColumn("DATE");
        table.AddColumn("READ");

        foreach (var msg in messages)
        {
            string readMark = msg.IsRead ? "[green]✓[/]" : "[yellow]•[/]";
            table.AddRow(
                Markup.Escape(Truncate(msg.From, 30)),
                Markup.Escape(Truncate(msg.Subject, 45)),
                msg.ReceivedDateTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                readMark
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteFolderTable(IReadOnlyList<MsGraphCli.Core.Models.MailFolder> folders)
    {
        var table = new Table();
        table.AddColumn("NAME");
        table.AddColumn(new TableColumn("TOTAL").RightAligned());
        table.AddColumn(new TableColumn("UNREAD").RightAligned());

        foreach (var folder in folders)
        {
            string unread = folder.UnreadItemCount > 0
                ? $"[yellow]{folder.UnreadItemCount}[/]"
                : folder.UnreadItemCount.ToString();

            table.AddRow(
                Markup.Escape(folder.DisplayName),
                folder.TotalItemCount.ToString(),
                unread
            );
        }

        AnsiConsole.Write(table);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}

/// <summary>
/// Writes results as tab-separated values for piping.
/// </summary>
public sealed class PlainOutputFormatter : IOutputFormatter
{
    public void WriteResult<T>(T data, TextWriter stdout)
    {
        // Generic fallback — specific commands override
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        stdout.WriteLine(json);
    }

    public void WriteError(string errorCode, string message, TextWriter stderr)
    {
        stderr.WriteLine($"ERROR\t{errorCode}\t{message}");
    }

    public void WriteMessage(string message, TextWriter stderr)
    {
        stderr.WriteLine(message);
    }
}

/// <summary>
/// Resolves the active output formatter based on flags and environment.
/// </summary>
public static class OutputFormatResolver
{
    public static IOutputFormatter Resolve(bool jsonFlag, bool plainFlag)
    {
        if (jsonFlag || Environment.GetEnvironmentVariable("MSGRAPH_JSON") == "1")
        {
            return new JsonOutputFormatter();
        }

        if (plainFlag || Environment.GetEnvironmentVariable("MSGRAPH_PLAIN") == "1")
        {
            return new PlainOutputFormatter();
        }

        // If stdout is not a TTY, default to plain
        if (!Console.IsOutputRedirected)
        {
            return new TableOutputFormatter();
        }

        return new PlainOutputFormatter();
    }
}
