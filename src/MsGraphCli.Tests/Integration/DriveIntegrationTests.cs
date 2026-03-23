using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for OneDrive operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class DriveIntegrationTests
{
    private static bool IsLiveTestEnabled =>
        Environment.GetEnvironmentVariable("MSGRAPH_LIVE") == "1";

    [Fact(Skip = "Requires MSGRAPH_LIVE=1 and authenticated session")]
    public void ListAndDownloadFile_Placeholder()
    {
        Assert.True(IsLiveTestEnabled, "Set MSGRAPH_LIVE=1 to run live tests");
    }
}
