using System.Globalization;
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
        string json = JsonSerializer.Serialize(data, FallbackJsonOptions);
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

    private static readonly JsonSerializerOptions FallbackJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
                msg.ReceivedDateTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
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
                : folder.UnreadItemCount.ToString(CultureInfo.InvariantCulture);

            table.AddRow(
                Markup.Escape(folder.DisplayName),
                folder.TotalItemCount.ToString(CultureInfo.InvariantCulture),
                unread
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteAttachmentTable(IReadOnlyList<MsGraphCli.Core.Models.MailAttachmentInfo> attachments)
    {
        var table = new Table();
        table.AddColumn("NAME");
        table.AddColumn("TYPE");
        table.AddColumn(new TableColumn("SIZE").RightAligned());

        foreach (var att in attachments)
        {
            string size = att.Size switch
            {
                >= 1024 * 1024 => $"{att.Size / (1024.0 * 1024.0):F1} MB",
                >= 1024 => $"{att.Size / 1024.0:F1} KB",
                _ => $"{att.Size.ToString(CultureInfo.InvariantCulture)} B",
            };

            table.AddRow(
                Markup.Escape(att.Name),
                Markup.Escape(att.ContentType),
                size
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteCalendarTable(IReadOnlyList<MsGraphCli.Core.Models.CalendarInfo> calendars)
    {
        var table = new Table();
        table.AddColumn("NAME");
        table.AddColumn("DEFAULT");
        table.AddColumn("ID");

        foreach (var cal in calendars)
        {
            table.AddRow(
                Markup.Escape(cal.Name),
                cal.IsDefault ? "[green]yes[/]" : "",
                Markup.Escape(Truncate(cal.Id, 40))
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteCalendarEventTable(IReadOnlyList<MsGraphCli.Core.Models.CalendarEventSummary> events)
    {
        var table = new Table();
        table.AddColumn("SUBJECT");
        table.AddColumn("START");
        table.AddColumn("END");
        table.AddColumn("LOCATION");
        table.AddColumn("STATUS");

        foreach (var evt in events)
        {
            string status = evt.IsCancelled ? "[red]cancelled[/]"
                : evt.ResponseStatus is not null ? Markup.Escape(evt.ResponseStatus) : "";

            string startStr = evt.IsAllDay
                ? evt.Start.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : evt.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            string endStr = evt.IsAllDay
                ? evt.End.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : evt.End.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            table.AddRow(
                Markup.Escape(Truncate(evt.Subject, 40)),
                startStr,
                endStr,
                Markup.Escape(Truncate(evt.Location ?? "", 25)),
                status
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteFreeBusyTable(IReadOnlyList<MsGraphCli.Core.Models.ScheduleResult> schedules)
    {
        var table = new Table();
        table.AddColumn("EMAIL");
        table.AddColumn("START");
        table.AddColumn("END");
        table.AddColumn("STATUS");

        foreach (var schedule in schedules)
        {
            foreach (var slot in schedule.Slots)
            {
                string statusColor = slot.Status switch
                {
                    "Free" or "free" => "[green]free[/]",
                    "Busy" or "busy" => "[red]busy[/]",
                    "Tentative" or "tentative" => "[yellow]tentative[/]",
                    _ => Markup.Escape(slot.Status),
                };

                table.AddRow(
                    Markup.Escape(schedule.Email),
                    slot.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    slot.End.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    statusColor
                );
            }
        }

        AnsiConsole.Write(table);
    }

    public static void WriteDriveItemTable(IReadOnlyList<MsGraphCli.Core.Models.DriveItemSummary> items)
    {
        var table = new Table();
        table.AddColumn("NAME");
        table.AddColumn(new TableColumn("SIZE").RightAligned());
        table.AddColumn("MODIFIED");
        table.AddColumn("TYPE");
        table.AddColumn("ID");

        foreach (var item in items)
        {
            string size = item.IsFolder ? "—" : item.Size switch
            {
                null => "—",
                >= 1024 * 1024 => $"{item.Size / (1024.0 * 1024.0):F1} MB",
                >= 1024 => $"{item.Size / 1024.0:F1} KB",
                _ => $"{item.Size?.ToString(CultureInfo.InvariantCulture)} B",
            };

            string modified = item.LastModified?.LocalDateTime
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";

            string type = item.IsFolder
                ? "[blue]folder[/]"
                : Markup.Escape(item.MimeType ?? "file");

            table.AddRow(
                Markup.Escape(Truncate(item.Name, 40)),
                size,
                modified,
                type,
                Markup.Escape(Truncate(item.Id, 40))
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteDriveItemDetailTable(MsGraphCli.Core.Models.DriveItemDetail item)
    {
        string type = item.IsFolder ? "folder" : item.MimeType ?? "file";

        string size;
        if (item.IsFolder)
        {
            size = "—";
        }
        else
        {
            size = item.Size switch
            {
                >= 1024 * 1024 => $"{item.Size / (1024.0 * 1024.0):F1} MB",
                >= 1024 => $"{item.Size / 1024.0:F1} KB",
                _ => $"{item.Size?.ToString(CultureInfo.InvariantCulture) ?? "—"} B",
            };
        }

        string created = item.Created?.LocalDateTime
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";
        string modified = item.LastModified?.LocalDateTime
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";

        Console.WriteLine($"Name:      {item.Name}");
        Console.WriteLine($"Type:      {type}");
        Console.WriteLine($"Size:      {size}");
        Console.WriteLine($"Created:   {created}");
        Console.WriteLine($"Modified:  {modified}");
        Console.WriteLine($"Path:      {item.ParentPath ?? "—"}");
        Console.WriteLine($"Web URL:   {item.WebUrl ?? "—"}");
        Console.WriteLine($"ID:        {item.Id}");
    }

    public static void WriteTaskListTable(IReadOnlyList<MsGraphCli.Core.Models.TaskListInfo> lists)
    {
        var table = new Table();
        table.AddColumn("NAME");
        table.AddColumn("DEFAULT");
        table.AddColumn("ID");

        foreach (var list in lists)
        {
            table.AddRow(
                Markup.Escape(list.DisplayName),
                list.IsDefaultList ? "[green]yes[/]" : "",
                Markup.Escape(Truncate(list.Id, 40))
            );
        }

        AnsiConsole.Write(table);
    }

    public static void WriteTodoTaskTable(IReadOnlyList<MsGraphCli.Core.Models.TodoTaskItem> tasks)
    {
        var table = new Table();
        table.AddColumn("TITLE");
        table.AddColumn("STATUS");
        table.AddColumn("DUE");
        table.AddColumn("IMPORTANCE");
        table.AddColumn("ID");

        foreach (var task in tasks)
        {
            string status = task.Status switch
            {
                "completed" => "[green]completed[/]",
                "inProgress" => "[yellow]inProgress[/]",
                "notStarted" => "[red]notStarted[/]",
                _ => Markup.Escape(task.Status),
            };

            string due = task.DueDate?.LocalDateTime
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

            string importance = task.Importance switch
            {
                "high" => "[red]high[/]",
                "normal" => "[yellow]normal[/]",
                "low" => "[dim]low[/]",
                _ => Markup.Escape(task.Importance),
            };

            table.AddRow(
                Markup.Escape(Truncate(task.Title, 40)),
                status,
                due,
                importance,
                Markup.Escape(Truncate(task.Id, 40))
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
        string json = JsonSerializer.Serialize(data, PlainFallbackJsonOptions);
        stdout.WriteLine(json);
    }

    private static readonly JsonSerializerOptions PlainFallbackJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
