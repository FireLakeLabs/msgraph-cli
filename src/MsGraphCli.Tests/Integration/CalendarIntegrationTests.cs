using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for calendar operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class CalendarIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ListCalendars_ReturnsAtLeastOne()
    {
        if (!IsLiveTestEnabled) return;

        CalendarService service = CreateCalendarService(readOnly: true);
        IReadOnlyList<CalendarInfo> calendars = await service.ListCalendarsAsync(CancellationToken.None);

        Assert.NotEmpty(calendars);
        Assert.Contains(calendars, c => c.IsDefault);
    }

    [Fact]
    public async Task CreateReadUpdateDelete_Event_RoundTrip()
    {
        if (!IsLiveTestEnabled) return;

        CalendarService service = CreateCalendarService();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7).Date.AddHours(14);
        DateTimeOffset end = start.AddMinutes(30);

        // Create
        var request = new CalendarEventCreateRequest(
            Subject: $"Integration Test {Guid.NewGuid():N}",
            Start: start,
            End: end,
            Body: "Created by integration test");

        CalendarEventDetail created = await service.CreateEventAsync(request, CancellationToken.None);
        Assert.NotNull(created.Id);
        Assert.Equal(request.Subject, created.Subject);

        try
        {
            // Read back
            CalendarEventDetail fetched = await service.GetEventAsync(created.Id, CancellationToken.None);
            Assert.Equal(created.Id, fetched.Id);
            Assert.Equal(request.Subject, fetched.Subject);

            // Update
            var update = new CalendarEventUpdateRequest(Subject: fetched.Subject + " UPDATED");
            CalendarEventDetail updated = await service.UpdateEventAsync(created.Id, update, CancellationToken.None);
            Assert.EndsWith(" UPDATED", updated.Subject);
        }
        finally
        {
            // Delete (cleanup)
            await service.DeleteEventAsync(created.Id, CancellationToken.None);
        }
    }
}
