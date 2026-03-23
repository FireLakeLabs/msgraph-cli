using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for calendar operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class CalendarIntegrationTests
{
    private static bool IsLiveTestEnabled =>
        Environment.GetEnvironmentVariable("MSGRAPH_LIVE") == "1";

    [Fact(Skip = "Requires MSGRAPH_LIVE=1 and authenticated session")]
    public void CreateReadDeleteEvent_Placeholder()
    {
        // Live test: create event, read it back, delete it
        Assert.True(IsLiveTestEnabled, "Set MSGRAPH_LIVE=1 to run live tests");
    }
}
