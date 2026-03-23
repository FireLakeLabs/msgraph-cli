using System.Globalization;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Me.Calendar.GetSchedule;
using MsGraphCli.Core.Models;

namespace MsGraphCli.Core.Services;

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEventSummary>> ListEventsAsync(
        DateTimeOffset start, DateTimeOffset endTime, string? calendarId, int? max, CancellationToken cancellationToken);

    Task<CalendarEventDetail> GetEventAsync(string eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEventSummary>> SearchEventsAsync(
        string query, DateTimeOffset? start, DateTimeOffset? endTime, int? max, CancellationToken cancellationToken);

    Task<CalendarEventDetail> CreateEventAsync(CalendarEventCreateRequest request, CancellationToken cancellationToken);

    Task<CalendarEventDetail> UpdateEventAsync(string eventId, CalendarEventUpdateRequest request, CancellationToken cancellationToken);

    Task DeleteEventAsync(string eventId, CancellationToken cancellationToken);

    Task RespondToEventAsync(string eventId, string response, string? message, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleResult>> GetFreeBusyAsync(
        IReadOnlyList<string> emails, DateTimeOffset start, DateTimeOffset endTime, CancellationToken cancellationToken);
}

public sealed class CalendarService : ICalendarService
{
    private readonly GraphServiceClient _client;

    private static readonly string[] EventSelect =
        ["id", "subject", "start", "end", "isAllDay", "location", "organizer", "isOrganizer",
         "responseStatus", "isCancelled"];

    private static readonly string[] EventDetailSelect =
        ["id", "subject", "start", "end", "isAllDay", "location", "organizer", "isOrganizer",
         "responseStatus", "isCancelled", "body", "attendees", "onlineMeeting", "recurrence"];

    public CalendarService(GraphServiceClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        var response = await _client.Me.Calendars
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "name", "color", "isDefaultCalendar"];
                config.QueryParameters.Top = 100;
            }, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(c => new CalendarInfo(
            Id: c.Id ?? "",
            Name: c.Name ?? "",
            Color: c.Color?.ToString(),
            IsDefault: c.IsDefaultCalendar ?? false
        )).ToList();
    }

    public async Task<IReadOnlyList<CalendarEventSummary>> ListEventsAsync(
        DateTimeOffset start, DateTimeOffset endTime, string? calendarId, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 50;
        string startUtc = start.UtcDateTime.ToString("o");
        string endUtc = endTime.UtcDateTime.ToString("o");

        EventCollectionResponse? response;

        if (!string.IsNullOrEmpty(calendarId))
        {
            response = await _client.Me.Calendars[calendarId].CalendarView
                .GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = startUtc;
                    config.QueryParameters.EndDateTime = endUtc;
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = EventSelect;
                    config.QueryParameters.Orderby = ["start/dateTime"];
                }, cancellationToken);
        }
        else
        {
            response = await _client.Me.CalendarView
                .GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = startUtc;
                    config.QueryParameters.EndDateTime = endUtc;
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = EventSelect;
                    config.QueryParameters.Orderby = ["start/dateTime"];
                }, cancellationToken);
        }

        return MapEventSummaries(response?.Value);
    }

    public async Task<CalendarEventDetail> GetEventAsync(string eventId, CancellationToken cancellationToken)
    {
        Event? evt = await _client.Me.Events[eventId]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = EventDetailSelect;
            }, cancellationToken);

        if (evt is null)
        {
            throw new InvalidOperationException($"Event '{eventId}' not found.");
        }

        return MapEventDetail(evt);
    }

    public async Task<IReadOnlyList<CalendarEventSummary>> SearchEventsAsync(
        string query, DateTimeOffset? start, DateTimeOffset? endTime, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 25;

        var response = await _client.Me.Events
            .GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = EventSelect;

                // Graph API does not support $search on Events.
                // Use $filter with contains() on subject instead.
                var filters = new List<string>
                {
                    $"contains(subject, '{query.Replace("'", "''")}')",
                };

                if (start.HasValue)
                {
                    filters.Add($"start/dateTime ge '{start.Value.UtcDateTime:o}'");
                }
                if (endTime.HasValue)
                {
                    filters.Add($"end/dateTime le '{endTime.Value.UtcDateTime:o}'");
                }

                config.QueryParameters.Filter = string.Join(" and ", filters);
            }, cancellationToken);

        return MapEventSummaries(response?.Value);
    }

    public async Task<CalendarEventDetail> CreateEventAsync(CalendarEventCreateRequest request, CancellationToken cancellationToken)
    {
        var newEvent = new Event
        {
            Subject = request.Subject,
            Start = ToDateTimeTimeZone(request.Start, request.IsAllDay),
            End = ToDateTimeTimeZone(request.End, request.IsAllDay),
            IsAllDay = request.IsAllDay,
        };

        if (request.Location is not null)
        {
            newEvent.Location = new Location { DisplayName = request.Location };
        }

        if (request.Body is not null)
        {
            newEvent.Body = new ItemBody
            {
                ContentType = string.Equals(request.BodyContentType, "HTML", StringComparison.OrdinalIgnoreCase)
                    ? BodyType.Html : BodyType.Text,
                Content = request.Body,
            };
        }

        if (request.Attendees is { Count: > 0 })
        {
            newEvent.Attendees = request.Attendees.Select(email => new Attendee
            {
                EmailAddress = new EmailAddress { Address = email },
                Type = AttendeeType.Required,
            }).ToList();
        }

        Event? created = await _client.Me.Events.PostAsync(newEvent, cancellationToken: cancellationToken);

        if (created is null)
        {
            throw new InvalidOperationException("Failed to create event.");
        }

        return MapEventDetail(created);
    }

    public async Task<CalendarEventDetail> UpdateEventAsync(string eventId, CalendarEventUpdateRequest request, CancellationToken cancellationToken)
    {
        var update = new Event();

        if (request.Subject is not null) update.Subject = request.Subject;
        if (request.Start.HasValue) update.Start = ToDateTimeTimeZone(request.Start.Value, request.IsAllDay ?? false);
        if (request.End.HasValue) update.End = ToDateTimeTimeZone(request.End.Value, request.IsAllDay ?? false);
        if (request.IsAllDay.HasValue) update.IsAllDay = request.IsAllDay.Value;
        if (request.Location is not null) update.Location = new Location { DisplayName = request.Location };

        if (request.Body is not null)
        {
            update.Body = new ItemBody
            {
                ContentType = string.Equals(request.BodyContentType, "HTML", StringComparison.OrdinalIgnoreCase)
                    ? BodyType.Html : BodyType.Text,
                Content = request.Body,
            };
        }

        if (request.Attendees is not null)
        {
            update.Attendees = request.Attendees.Select(email => new Attendee
            {
                EmailAddress = new EmailAddress { Address = email },
                Type = AttendeeType.Required,
            }).ToList();
        }

        Event? updated = await _client.Me.Events[eventId].PatchAsync(update, cancellationToken: cancellationToken);

        if (updated is null)
        {
            throw new InvalidOperationException($"Failed to update event '{eventId}'.");
        }

        return MapEventDetail(updated);
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await _client.Me.Events[eventId].DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task RespondToEventAsync(string eventId, string response, string? message, CancellationToken cancellationToken)
    {
        switch (response.ToLowerInvariant())
        {
            case "accept":
                await _client.Me.Events[eventId].Accept.PostAsync(
                    new Microsoft.Graph.Me.Events.Item.Accept.AcceptPostRequestBody
                    {
                        Comment = message,
                        SendResponse = true,
                    }, cancellationToken: cancellationToken);
                break;

            case "decline":
                await _client.Me.Events[eventId].Decline.PostAsync(
                    new Microsoft.Graph.Me.Events.Item.Decline.DeclinePostRequestBody
                    {
                        Comment = message,
                        SendResponse = true,
                    }, cancellationToken: cancellationToken);
                break;

            case "tentative":
                await _client.Me.Events[eventId].TentativelyAccept.PostAsync(
                    new Microsoft.Graph.Me.Events.Item.TentativelyAccept.TentativelyAcceptPostRequestBody
                    {
                        Comment = message,
                        SendResponse = true,
                    }, cancellationToken: cancellationToken);
                break;

            default:
                throw new ArgumentException($"Invalid response '{response}'. Use 'accept', 'decline', or 'tentative'.");
        }
    }

    public async Task<IReadOnlyList<ScheduleResult>> GetFreeBusyAsync(
        IReadOnlyList<string> emails, DateTimeOffset start, DateTimeOffset endTime, CancellationToken cancellationToken)
    {
        var requestBody = new GetSchedulePostRequestBody
        {
            Schedules = emails.ToList(),
            StartTime = new DateTimeTimeZone
            {
                DateTime = start.UtcDateTime.ToString("o"),
                TimeZone = "UTC",
            },
            EndTime = new DateTimeTimeZone
            {
                DateTime = endTime.UtcDateTime.ToString("o"),
                TimeZone = "UTC",
            },
        };

        var response = await _client.Me.Calendar.GetSchedule
            .PostAsGetSchedulePostResponseAsync(requestBody, cancellationToken: cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(schedule => new ScheduleResult(
            Email: schedule.ScheduleId ?? "",
            Slots: schedule.ScheduleItems?.Select(item => new FreeBusySlot(
                Start: DateTimeOffset.Parse(item.Start?.DateTime ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                End: DateTimeOffset.Parse(item.End?.DateTime ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Status: item.Status?.ToString() ?? "unknown"
            )).ToList() ?? (IReadOnlyList<FreeBusySlot>)[]
        )).ToList();
    }

    // ── Mapping helpers ──

    private static List<CalendarEventSummary> MapEventSummaries(List<Event>? events)
    {
        if (events is null)
        {
            return [];
        }

        return events.Select(e => new CalendarEventSummary(
            Id: e.Id ?? "",
            Subject: e.Subject ?? "(no subject)",
            Start: ParseGraphDateTime(e.Start),
            End: ParseGraphDateTime(e.End),
            IsAllDay: e.IsAllDay ?? false,
            Location: e.Location?.DisplayName,
            OrganizerEmail: e.Organizer?.EmailAddress?.Address,
            IsOrganizer: e.IsOrganizer ?? false,
            ResponseStatus: e.ResponseStatus?.Response?.ToString(),
            IsCancelled: e.IsCancelled ?? false
        )).ToList();
    }

    private static CalendarEventDetail MapEventDetail(Event e) => new(
        Id: e.Id ?? "",
        Subject: e.Subject ?? "(no subject)",
        Start: ParseGraphDateTime(e.Start),
        End: ParseGraphDateTime(e.End),
        IsAllDay: e.IsAllDay ?? false,
        Location: e.Location?.DisplayName,
        OrganizerEmail: e.Organizer?.EmailAddress?.Address,
        IsOrganizer: e.IsOrganizer ?? false,
        ResponseStatus: e.ResponseStatus?.Response?.ToString(),
        IsCancelled: e.IsCancelled ?? false,
        BodyText: e.Body?.ContentType == BodyType.Text ? e.Body?.Content : null,
        BodyHtml: e.Body?.ContentType == BodyType.Html ? e.Body?.Content : null,
        Attendees: e.Attendees?.Select(a => new CalendarAttendee(
            Email: a.EmailAddress?.Address ?? "",
            Name: a.EmailAddress?.Name,
            ResponseStatus: a.Status?.Response?.ToString()
        )).ToList() ?? (IReadOnlyList<CalendarAttendee>)[],
        OnlineMeetingUrl: e.OnlineMeeting?.JoinUrl,
        Recurrence: e.Recurrence?.Pattern?.Type?.ToString()
    );

    private static DateTimeTimeZone ToDateTimeTimeZone(DateTimeOffset dto, bool isAllDay)
    {
        if (isAllDay)
        {
            return new DateTimeTimeZone
            {
                DateTime = dto.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeZone = "UTC",
            };
        }

        return new DateTimeTimeZone
        {
            DateTime = dto.UtcDateTime.ToString("o"),
            TimeZone = "UTC",
        };
    }

    private static DateTimeOffset ParseGraphDateTime(DateTimeTimeZone? dtz)
    {
        if (dtz?.DateTime is null)
        {
            return DateTimeOffset.MinValue;
        }

        if (DateTimeOffset.TryParse(dtz.DateTime, out DateTimeOffset result))
        {
            return result;
        }

        return DateTimeOffset.MinValue;
    }
}
