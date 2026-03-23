namespace MsGraphCli.Core.Models;

public record CalendarInfo(
    string Id,
    string Name,
    string? Color,
    bool IsDefault
);

public record CalendarEventSummary(
    string Id,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? OrganizerEmail,
    bool IsOrganizer,
    string? ResponseStatus,
    bool IsCancelled
);

public record CalendarEventDetail(
    string Id,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? OrganizerEmail,
    bool IsOrganizer,
    string? ResponseStatus,
    bool IsCancelled,
    string? BodyText,
    string? BodyHtml,
    IReadOnlyList<CalendarAttendee> Attendees,
    string? OnlineMeetingUrl,
    string? Recurrence
);

public record CalendarAttendee(
    string Email,
    string? Name,
    string? ResponseStatus
);

public record CalendarEventCreateRequest(
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay = false,
    string? Location = null,
    IReadOnlyList<string>? Attendees = null,
    string? Body = null,
    string BodyContentType = "Text"
);

public record CalendarEventUpdateRequest(
    string? Subject = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    bool? IsAllDay = null,
    string? Location = null,
    IReadOnlyList<string>? Attendees = null,
    string? Body = null,
    string? BodyContentType = null
);

public record FreeBusySlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Status
);

public record ScheduleResult(
    string Email,
    IReadOnlyList<FreeBusySlot> Slots
);
