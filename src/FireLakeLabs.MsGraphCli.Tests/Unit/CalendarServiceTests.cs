using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Core.Models;
using FireLakeLabs.MsGraphCli.Core.Services;
using FireLakeLabs.MsGraphCli.Tests.Unit.Helpers;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class CalendarServiceTests
{
    private static readonly DateTimeOffset TestStart = new(2025, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TestEnd = new(2025, 7, 15, 17, 0, 0, TimeSpan.Zero);

    // ── ListCalendarsAsync ──

    [Fact]
    public async Task ListCalendars_ReturnsCalendarInfos()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [
                { "id": "cal-1", "name": "Calendar", "color": "auto", "isDefaultCalendar": true },
                { "id": "cal-2", "name": "Work", "color": "lightBlue", "isDefaultCalendar": false }
            ]
        }
        """);
        CalendarService svc = CreateService(handler);

        IReadOnlyList<CalendarInfo> result = await svc.ListCalendarsAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("cal-1");
        result[0].Name.Should().Be("Calendar");
        result[0].IsDefault.Should().BeTrue();
        result[1].Name.Should().Be("Work");
        result[1].IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task ListCalendars_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        CalendarService svc = CreateService(handler);

        IReadOnlyList<CalendarInfo> result = await svc.ListCalendarsAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── ListEventsAsync ──

    [Fact]
    public async Task ListEvents_NoCalendarId_CallsCalendarViewEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.ListEventsAsync(TestStart, TestEnd, calendarId: null, max: null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/calendarView");
        handler.LastRequest.Uri.AbsolutePath.Should().NotContain("/calendars/");
    }

    [Fact]
    public async Task ListEvents_WithCalendarId_CallsCalendarCalendarViewEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.ListEventsAsync(TestStart, TestEnd, calendarId: "cal-123", max: null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/calendars/cal-123/calendarView");
    }

    [Fact]
    public async Task ListEvents_DefaultMax_Uses50()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.ListEventsAsync(TestStart, TestEnd, null, max: null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=50");
    }

    [Fact]
    public async Task ListEvents_CustomMax_UsesProvidedValue()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.ListEventsAsync(TestStart, TestEnd, null, max: 10, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=10");
    }

    [Fact]
    public async Task ListEvents_MapsFieldsCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue($$"""
        {
            "value": [{
                "id": "evt-1",
                "subject": "Team Meeting",
                "start": { "dateTime": "2025-07-15T10:00:00.0000000", "timeZone": "UTC" },
                "end": { "dateTime": "2025-07-15T11:00:00.0000000", "timeZone": "UTC" },
                "isAllDay": false,
                "location": { "displayName": "Room A" },
                "organizer": { "emailAddress": { "address": "org@test.com" } },
                "isOrganizer": true,
                "responseStatus": { "response": "accepted" },
                "isCancelled": false
            }]
        }
        """);
        CalendarService svc = CreateService(handler);

        IReadOnlyList<CalendarEventSummary> result =
            await svc.ListEventsAsync(TestStart, TestEnd, null, null, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("evt-1");
        result[0].Subject.Should().Be("Team Meeting");
        result[0].IsAllDay.Should().BeFalse();
        result[0].Location.Should().Be("Room A");
        result[0].OrganizerEmail.Should().Be("org@test.com");
        result[0].IsOrganizer.Should().BeTrue();
        result[0].IsCancelled.Should().BeFalse();
    }

    // ── GetEventAsync ──

    [Fact]
    public async Task GetEvent_ReturnsEventDetail()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "id": "evt-1",
            "subject": "Detail Test",
            "start": { "dateTime": "2025-07-15T10:00:00.0000000", "timeZone": "UTC" },
            "end": { "dateTime": "2025-07-15T11:00:00.0000000", "timeZone": "UTC" },
            "isAllDay": false,
            "location": { "displayName": "Teams" },
            "organizer": { "emailAddress": { "address": "org@test.com" } },
            "isOrganizer": true,
            "responseStatus": { "response": "accepted" },
            "isCancelled": false,
            "body": { "contentType": "html", "content": "<p>Agenda</p>" },
            "attendees": [{
                "emailAddress": { "address": "bob@test.com", "name": "Bob" },
                "status": { "response": "accepted" },
                "type": "required"
            }],
            "onlineMeeting": { "joinUrl": "https://teams.live.com/meet/123" },
            "recurrence": { "pattern": { "type": "weekly" } }
        }
        """);
        CalendarService svc = CreateService(handler);

        CalendarEventDetail detail = await svc.GetEventAsync("evt-1", CancellationToken.None);

        detail.Id.Should().Be("evt-1");
        detail.Subject.Should().Be("Detail Test");
        detail.Location.Should().Be("Teams");
        detail.BodyHtml.Should().Be("<p>Agenda</p>");
        detail.BodyText.Should().BeNull();
        detail.Attendees.Should().HaveCount(1);
        detail.Attendees[0].Email.Should().Be("bob@test.com");
        detail.Attendees[0].Name.Should().Be("Bob");
        detail.OnlineMeetingUrl.Should().Be("https://teams.live.com/meet/123");
        detail.Recurrence.Should().Be("Weekly");
    }

    [Fact]
    public async Task GetEvent_TextBody_SetsBodyTextOnly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson(bodyContentType: "text", bodyContent: "Plain text"));
        CalendarService svc = CreateService(handler);

        CalendarEventDetail detail = await svc.GetEventAsync("evt-1", CancellationToken.None);

        detail.BodyText.Should().Be("Plain text");
        detail.BodyHtml.Should().BeNull();
    }

    // ── SearchEventsAsync ──

    [Fact]
    public async Task SearchEvents_BasicQuery_UsesContainsFilter()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.SearchEventsAsync("standup", null, null, null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("contains(subject, 'standup')");
    }

    [Fact]
    public async Task SearchEvents_QueryWithSingleQuote_EscapesQuotes()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.SearchEventsAsync("team's meeting", null, null, null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("team''s meeting");
    }

    [Fact]
    public async Task SearchEvents_WithStartAndEnd_AddsDateFilters()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.SearchEventsAsync("test", TestStart, TestEnd, null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("start/dateTime ge");
        query.Should().Contain("end/dateTime le");
    }

    [Fact]
    public async Task SearchEvents_NoStartOrEnd_OnlyContainsFilter()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.SearchEventsAsync("test", null, null, null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("contains(subject, 'test')");
        query.Should().NotContain("start/dateTime ge");
        query.Should().NotContain("end/dateTime le");
    }

    [Fact]
    public async Task SearchEvents_DefaultMax_Uses25()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.SearchEventsAsync("test", null, null, null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=25");
    }

    // ── CreateEventAsync ──

    [Fact]
    public async Task CreateEvent_MinimalRequest_PostsToEventsEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Quick Sync",
            Start: TestStart,
            End: TestEnd);

        CalendarEventDetail result = await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/events");
        handler.LastRequest.Body.Should().Contain("Quick Sync");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateEvent_WithLocation_SetsLocation()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Meeting",
            Start: TestStart,
            End: TestEnd,
            Location: "Conference Room B");

        await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("Conference Room B");
    }

    [Fact]
    public async Task CreateEvent_WithBody_SetsBody()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Meeting",
            Start: TestStart,
            End: TestEnd,
            Body: "Discuss roadmap");

        await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("Discuss roadmap");
    }

    [Fact]
    public async Task CreateEvent_HtmlBody_SetsHtmlContentType()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Meeting",
            Start: TestStart,
            End: TestEnd,
            Body: "<p>Agenda</p>",
            BodyContentType: "HTML");

        await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("html");
    }

    [Fact]
    public async Task CreateEvent_WithAttendees_SetsAttendees()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Meeting",
            Start: TestStart,
            End: TestEnd,
            Attendees: ["alice@test.com", "bob@test.com"]);

        await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("alice@test.com");
        handler.LastRequest.Body.Should().Contain("bob@test.com");
    }

    [Fact]
    public async Task CreateEvent_NoOptionalFields_OmitsThem()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventCreateRequest(
            Subject: "Simple",
            Start: TestStart,
            End: TestEnd);

        await svc.CreateEventAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().NotContain("attendees");
    }

    // ── UpdateEventAsync ──

    [Fact]
    public async Task UpdateEvent_SubjectOnly_PatchesSubject()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventUpdateRequest(Subject: "Updated Title");

        await svc.UpdateEventAsync("evt-1", request, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/events/evt-1");
        handler.LastRequest.Body.Should().Contain("Updated Title");
    }

    [Fact]
    public async Task UpdateEvent_AllFields_PatchesAllFields()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(EventJson());
        CalendarService svc = CreateService(handler);

        var request = new CalendarEventUpdateRequest(
            Subject: "Full Update",
            Start: TestStart,
            End: TestEnd,
            IsAllDay: false,
            Location: "Room C",
            Body: "Updated body",
            Attendees: ["new@test.com"]);

        await svc.UpdateEventAsync("evt-1", request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("Full Update");
        handler.LastRequest.Body.Should().Contain("Room C");
        handler.LastRequest.Body.Should().Contain("Updated body");
        handler.LastRequest.Body.Should().Contain("new@test.com");
    }

    // ── DeleteEventAsync ──

    [Fact]
    public async Task DeleteEvent_CallsDeleteEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.DeleteEventAsync("evt-1", CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/events/evt-1");
    }

    // ── RespondToEventAsync ──

    [Fact]
    public async Task Respond_Accept_PostsToAcceptEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.RespondToEventAsync("evt-1", "accept", null, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/accept");
    }

    [Fact]
    public async Task Respond_Decline_PostsToDeclineEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.RespondToEventAsync("evt-1", "decline", null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/decline");
    }

    [Fact]
    public async Task Respond_Tentative_PostsToTentativelyAcceptEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.RespondToEventAsync("evt-1", "tentative", null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/tentativelyAccept");
    }

    [Fact]
    public async Task Respond_InvalidResponse_ThrowsMsGraphCliException()
    {
        var handler = new MockGraphHandler();
        CalendarService svc = CreateService(handler);

        Func<Task> act = () => svc.RespondToEventAsync("evt-1", "maybe", null, CancellationToken.None);

        MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
        ex.ErrorCode.Should().Be("InvalidArgument");
    }

    [Fact]
    public async Task Respond_CaseInsensitive_Works()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.RespondToEventAsync("evt-1", "ACCEPT", null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/accept");
    }

    [Fact]
    public async Task Respond_WithMessage_IncludesComment()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        CalendarService svc = CreateService(handler);

        await svc.RespondToEventAsync("evt-1", "decline", "Conflict with another meeting", CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("Conflict with another meeting");
    }

    // ── GetFreeBusyAsync ──

    [Fact]
    public async Task GetFreeBusy_PostsToGetScheduleEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.GetFreeBusyAsync(["user@test.com"], TestStart, TestEnd, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/getSchedule");
    }

    [Fact]
    public async Task GetFreeBusy_SetsSchedulesAndTimeRange()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        CalendarService svc = CreateService(handler);

        await svc.GetFreeBusyAsync(["alice@test.com", "bob@test.com"], TestStart, TestEnd, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("alice@test.com");
        handler.LastRequest.Body.Should().Contain("bob@test.com");
        handler.LastRequest.Body.Should().Contain("UTC");
    }

    [Fact]
    public async Task GetFreeBusy_MapsResultCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [{
                "scheduleId": "alice@test.com",
                "scheduleItems": [{
                    "start": { "dateTime": "2025-07-15T10:00:00.0000000", "timeZone": "UTC" },
                    "end": { "dateTime": "2025-07-15T11:00:00.0000000", "timeZone": "UTC" },
                    "status": "busy"
                }]
            }]
        }
        """);
        CalendarService svc = CreateService(handler);

        IReadOnlyList<ScheduleResult> result =
            await svc.GetFreeBusyAsync(["alice@test.com"], TestStart, TestEnd, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Email.Should().Be("alice@test.com");
        result[0].Slots.Should().HaveCount(1);
        result[0].Slots[0].Status.Should().Be("Busy");
    }

    [Fact]
    public async Task GetFreeBusy_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        CalendarService svc = CreateService(handler);

        IReadOnlyList<ScheduleResult> result =
            await svc.GetFreeBusyAsync(["user@test.com"], TestStart, TestEnd, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── Helpers ──

    private static CalendarService CreateService(MockGraphHandler handler)
    {
        return new CalendarService(MockGraphHandler.CreateClient(handler));
    }

    private static string EventJson(string bodyContentType = "html", string bodyContent = "<p>Body</p>") => $$"""
    {
        "id": "evt-1",
        "subject": "Test Event",
        "start": { "dateTime": "2025-07-15T10:00:00.0000000", "timeZone": "UTC" },
        "end": { "dateTime": "2025-07-15T11:00:00.0000000", "timeZone": "UTC" },
        "isAllDay": false,
        "location": { "displayName": "Room A" },
        "organizer": { "emailAddress": { "address": "org@test.com" } },
        "isOrganizer": true,
        "responseStatus": { "response": "accepted" },
        "isCancelled": false,
        "body": { "contentType": "{{bodyContentType}}", "content": "{{bodyContent}}" },
        "attendees": [],
        "onlineMeeting": null,
        "recurrence": null
    }
    """;
}
