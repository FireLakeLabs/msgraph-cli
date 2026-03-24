using System.CommandLine;
using System.Globalization;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Middleware;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class CalendarCommands
{
    public static Command Build(GlobalOptions global)
    {
        var calendarCommand = new Command("calendar", "Calendar operations");

        calendarCommand.Subcommands.Add(BuildListCalendars(global));
        calendarCommand.Subcommands.Add(BuildEvents(global));
        calendarCommand.Subcommands.Add(BuildGet(global));
        calendarCommand.Subcommands.Add(BuildSearch(global));
        calendarCommand.Subcommands.Add(BuildCreate(global));
        calendarCommand.Subcommands.Add(BuildUpdate(global));
        calendarCommand.Subcommands.Add(BuildDelete(global));
        calendarCommand.Subcommands.Add(BuildRespond(global));
        calendarCommand.Subcommands.Add(BuildFreeBusy(global));

        return calendarCommand;
    }

    private static (ICalendarService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global, bool readOnly = true)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["calendar"], readOnly: readOnly);
        HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
        var factory = new GraphClientFactory(authProvider, scopes, httpClient);
        bool useBeta = parseResult.GetValue(global.Beta);
        var client = factory.CreateClient(useBeta: useBeta);
        var service = new CalendarService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── msgraph calendar list ──

    private static Command BuildListCalendars(GlobalOptions global)
    {
        var command = new Command("list", "List calendars");

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            IReadOnlyList<CalendarInfo> calendars = await service.ListCalendarsAsync(cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { calendars }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteCalendarTable(calendars);
            }
            else
            {
                foreach (CalendarInfo cal in calendars)
                {
                    Console.WriteLine($"{cal.Name}\t{(cal.IsDefault ? "default" : "")}\t{cal.Id}");
                }
            }
        });

        return command;
    }

    // ── msgraph calendar events ──

    private static Command BuildEvents(GlobalOptions global)
    {
        var todayOption = new Option<bool>("--today") { Description = "Show today's events" };
        var tomorrowOption = new Option<bool>("--tomorrow") { Description = "Show tomorrow's events" };
        var weekOption = new Option<bool>("--week") { Description = "Show this week's events" };
        var daysOption = new Option<int?>("--days") { Description = "Show events for next N days" };
        var fromOption = new Option<string?>("--from") { Description = "Start date (yyyy-MM-dd or ISO 8601)" };
        var toOption = new Option<string?>("--to") { Description = "End date (yyyy-MM-dd or ISO 8601)" };
        var calendarOption = new Option<string?>("--calendar") { Description = "Calendar ID" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum number of events" };

        var command = new Command("events", "List calendar events");
        command.Options.Add(todayOption);
        command.Options.Add(tomorrowOption);
        command.Options.Add(weekOption);
        command.Options.Add(daysOption);
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(calendarOption);
        command.Options.Add(maxOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            (DateTimeOffset start, DateTimeOffset endTime) = ResolveTimeRange(parseResult,
                todayOption, tomorrowOption, weekOption, daysOption, fromOption, toOption);
            string? calendarId = parseResult.GetValue(calendarOption);
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<CalendarEventSummary> events = await service.ListEventsAsync(
                start, endTime, calendarId, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { events }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteCalendarEventTable(events);
            }
            else
            {
                foreach (CalendarEventSummary evt in events)
                {
                    Console.WriteLine($"{evt.Subject}\t{evt.Start:u}\t{evt.End:u}\t{evt.Location}");
                }
            }
        });

        return command;
    }

    // ── msgraph calendar get ──

    private static Command BuildGet(GlobalOptions global)
    {
        var eventIdArgument = new Argument<string>("eventId") { Description = "Event ID" };

        var command = new Command("get", "Get event details");
        command.Arguments.Add(eventIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string eventId = parseResult.GetValue(eventIdArgument)!;
            CalendarEventDetail evt = await service.GetEventAsync(eventId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { @event = evt }, Console.Out);
            }
            else
            {
                Console.WriteLine($"Subject:   {evt.Subject}");
                Console.WriteLine($"Start:     {evt.Start:u}");
                Console.WriteLine($"End:       {evt.End:u}");
                Console.WriteLine($"All Day:   {(evt.IsAllDay ? "yes" : "no")}");

                if (evt.Location is not null)
                {
                    Console.WriteLine($"Location:  {evt.Location}");
                }

                Console.WriteLine($"Organizer: {evt.OrganizerEmail}");
                Console.WriteLine($"Status:    {evt.ResponseStatus}");

                if (evt.OnlineMeetingUrl is not null)
                {
                    Console.WriteLine($"Meeting:   {evt.OnlineMeetingUrl}");
                }

                if (evt.Attendees.Count > 0)
                {
                    Console.WriteLine($"Attendees: {string.Join(", ", evt.Attendees.Select(a => $"{a.Email} ({a.ResponseStatus})"))}");
                }

                if (evt.BodyText is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("── Body ──");
                    Console.WriteLine(evt.BodyText);
                }
            }
        });

        return command;
    }

    // ── msgraph calendar search ──

    private static Command BuildSearch(GlobalOptions global)
    {
        var queryArgument = new Argument<string>("query") { Description = "Search query" };
        var fromOption = new Option<string?>("--from") { Description = "Start date filter" };
        var toOption = new Option<string?>("--to") { Description = "End date filter" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum results" };

        var command = new Command("search", "Search calendar events");
        command.Arguments.Add(queryArgument);
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(maxOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string query = parseResult.GetValue(queryArgument)!;
            DateTimeOffset? start = ParseOptionalDate(parseResult.GetValue(fromOption));
            DateTimeOffset? endTime = ParseOptionalDate(parseResult.GetValue(toOption));
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<CalendarEventSummary> events = await service.SearchEventsAsync(
                query, start, endTime, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { events, query }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteCalendarEventTable(events);
            }
            else
            {
                foreach (CalendarEventSummary evt in events)
                {
                    Console.WriteLine($"{evt.Subject}\t{evt.Start:u}\t{evt.End:u}\t{evt.Location}");
                }
            }
        });

        return command;
    }

    // ── msgraph calendar create ──

    private static Command BuildCreate(GlobalOptions global)
    {
        var subjectOption = new Option<string>("--subject") { Description = "Event subject", Required = true };
        var startOption = new Option<string>("--start") { Description = "Start date/time (ISO 8601)", Required = true };
        var endOption = new Option<string>("--end") { Description = "End date/time (ISO 8601)", Required = true };
        var attendeesOption = new Option<string?>("--attendees") { Description = "Attendee emails, comma-separated" };
        var locationOption = new Option<string?>("--location") { Description = "Event location" };
        var bodyOption = new Option<string?>("--body") { Description = "Event body text" };
        var allDayOption = new Option<bool>("--all-day") { Description = "Create as all-day event" };

        var command = new Command("create", "Create a calendar event");
        command.Options.Add(subjectOption);
        command.Options.Add(startOption);
        command.Options.Add(endOption);
        command.Options.Add(attendeesOption);
        command.Options.Add(locationOption);
        command.Options.Add(bodyOption);
        command.Options.Add(allDayOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("calendar create", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string startRaw = parseResult.GetValue(startOption)!;
            string endRaw = parseResult.GetValue(endOption)!;
            DateTimeOffset start = DateTimeOffset.Parse(startRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset endTime = DateTimeOffset.Parse(endRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            string? attendeesRaw = parseResult.GetValue(attendeesOption);
            List<string>? attendees = attendeesRaw is not null
                ? attendeesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : null;

            var request = new CalendarEventCreateRequest(
                Subject: parseResult.GetValue(subjectOption)!,
                Start: start,
                End: endTime,
                IsAllDay: parseResult.GetValue(allDayOption),
                Location: parseResult.GetValue(locationOption),
                Attendees: attendees,
                Body: parseResult.GetValue(bodyOption)
            );

            CalendarEventDetail created = await service.CreateEventAsync(request, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "created", @event = created }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Created: {created.Subject} ({created.Start:u} - {created.End:u})");
                Console.Error.WriteLine($"  ID: {created.Id}");
            }
        });

        return command;
    }

    // ── msgraph calendar update ──

    private static Command BuildUpdate(GlobalOptions global)
    {
        var eventIdArgument = new Argument<string>("eventId") { Description = "Event ID to update" };
        var subjectOption = new Option<string?>("--subject") { Description = "New subject" };
        var startOption = new Option<string?>("--start") { Description = "New start date/time" };
        var endOption = new Option<string?>("--end") { Description = "New end date/time" };
        var locationOption = new Option<string?>("--location") { Description = "New location" };
        var attendeesOption = new Option<string?>("--attendees") { Description = "New attendees (comma-separated)" };

        var command = new Command("update", "Update a calendar event");
        command.Arguments.Add(eventIdArgument);
        command.Options.Add(subjectOption);
        command.Options.Add(startOption);
        command.Options.Add(endOption);
        command.Options.Add(locationOption);
        command.Options.Add(attendeesOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("calendar update", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string eventId = parseResult.GetValue(eventIdArgument)!;

            string? startRaw = parseResult.GetValue(startOption);
            string? endRaw = parseResult.GetValue(endOption);
            string? attendeesRaw = parseResult.GetValue(attendeesOption);

            var request = new CalendarEventUpdateRequest(
                Subject: parseResult.GetValue(subjectOption),
                Start: startRaw is not null ? DateTimeOffset.Parse(startRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
                End: endRaw is not null ? DateTimeOffset.Parse(endRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
                Location: parseResult.GetValue(locationOption),
                Attendees: attendeesRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            );

            CalendarEventDetail updated = await service.UpdateEventAsync(eventId, request, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "updated", @event = updated }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Updated: {updated.Subject}");
            }
        });

        return command;
    }

    // ── msgraph calendar delete ──

    private static Command BuildDelete(GlobalOptions global)
    {
        var eventIdArgument = new Argument<string>("eventId") { Description = "Event ID to delete" };

        var command = new Command("delete", "Delete a calendar event");
        command.Arguments.Add(eventIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("calendar delete", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string eventId = parseResult.GetValue(eventIdArgument)!;
            await service.DeleteEventAsync(eventId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "deleted", eventId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("Event deleted.");
            }
        });

        return command;
    }

    // ── msgraph calendar respond ──

    private static Command BuildRespond(GlobalOptions global)
    {
        var eventIdArgument = new Argument<string>("eventId") { Description = "Event ID" };
        var statusOption = new Option<string>("--status") { Description = "Response: accept, decline, or tentative", Required = true };
        var messageOption = new Option<string?>("--message") { Description = "Optional response message" };

        var command = new Command("respond", "Respond to a calendar event invitation");
        command.Arguments.Add(eventIdArgument);
        command.Options.Add(statusOption);
        command.Options.Add(messageOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("calendar respond", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string eventId = parseResult.GetValue(eventIdArgument)!;
            string status = parseResult.GetValue(statusOption)!;
            string? message = parseResult.GetValue(messageOption);

            await service.RespondToEventAsync(eventId, status, message, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = $"responded_{status}", eventId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Response '{status}' sent.");
            }
        });

        return command;
    }

    // ── msgraph calendar freebusy ──

    private static Command BuildFreeBusy(GlobalOptions global)
    {
        var fromOption = new Option<string>("--from") { Description = "Start date/time (ISO 8601)", Required = true };
        var toOption = new Option<string>("--to") { Description = "End date/time (ISO 8601)", Required = true };
        var emailsOption = new Option<string?>("--emails") { Description = "Email addresses, comma-separated (default: self)" };

        var command = new Command("freebusy", "Check free/busy availability");
        command.Options.Add(fromOption);
        command.Options.Add(toOption);
        command.Options.Add(emailsOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string fromRaw = parseResult.GetValue(fromOption)!;
            string toRaw = parseResult.GetValue(toOption)!;
            DateTimeOffset start = DateTimeOffset.Parse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset endTime = DateTimeOffset.Parse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            string? emailsRaw = parseResult.GetValue(emailsOption);
            List<string> emails = emailsRaw is not null
                ? emailsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : ["me"];

            IReadOnlyList<ScheduleResult> results = await service.GetFreeBusyAsync(emails, start, endTime, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { schedules = results }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteFreeBusyTable(results);
            }
            else
            {
                foreach (ScheduleResult schedule in results)
                {
                    foreach (FreeBusySlot slot in schedule.Slots)
                    {
                        Console.WriteLine($"{schedule.Email}\t{slot.Start:u}\t{slot.End:u}\t{slot.Status}");
                    }
                }
            }
        });

        return command;
    }

    // ── Time range resolution ──

    private static (DateTimeOffset Start, DateTimeOffset End) ResolveTimeRange(
        ParseResult parseResult,
        Option<bool> todayOption,
        Option<bool> tomorrowOption,
        Option<bool> weekOption,
        Option<int?> daysOption,
        Option<string?> fromOption,
        Option<string?> toOption)
    {
        DateTimeOffset today = DateTimeOffset.Now.Date;

        string? fromRaw = parseResult.GetValue(fromOption);
        string? toRaw = parseResult.GetValue(toOption);

        if (fromRaw is not null || toRaw is not null)
        {
            DateTimeOffset start = fromRaw is not null
                ? DateTimeOffset.Parse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : today;
            DateTimeOffset endTime = toRaw is not null
                ? DateTimeOffset.Parse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : start.AddDays(7);
            return (start, endTime);
        }

        if (parseResult.GetValue(todayOption))
        {
            return (today, today.AddDays(1));
        }

        if (parseResult.GetValue(tomorrowOption))
        {
            return (today.AddDays(1), today.AddDays(2));
        }

        int? days = parseResult.GetValue(daysOption);
        if (days.HasValue)
        {
            return (today, today.AddDays(days.Value));
        }

        // Default: --week
        return (today, today.AddDays(7));
    }

    private static DateTimeOffset? ParseOptionalDate(string? value)
    {
        if (value is null) return null;
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
