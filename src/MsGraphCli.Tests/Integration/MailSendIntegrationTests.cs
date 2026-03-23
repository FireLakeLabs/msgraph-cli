using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for mail send operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class MailSendIntegrationTests
{
    private static bool IsLiveTestEnabled =>
        Environment.GetEnvironmentVariable("MSGRAPH_LIVE") == "1";

    [Fact(Skip = "Requires MSGRAPH_LIVE=1 and authenticated session")]
    public void SendMessage_Placeholder()
    {
        // Live test: dotnet run --project src/MsGraphCli -- mail send --to self@domain.com --subject "test" --body "hello" --json
        Assert.True(IsLiveTestEnabled, "Set MSGRAPH_LIVE=1 to run live tests");
    }
}
