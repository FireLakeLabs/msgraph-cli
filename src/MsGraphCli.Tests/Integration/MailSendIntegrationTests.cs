using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for mail operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class MailSendIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ListMessages_ReturnsResults()
    {
        if (!IsLiveTestEnabled) return;

        MailService service = CreateMailService(readOnly: true);
        IReadOnlyList<MailMessageSummary> messages = await service.ListMessagesAsync(null, 5, CancellationToken.None);

        Assert.NotNull(messages);
        // Inbox may be empty, so just verify the call succeeded
    }

    [Fact]
    public async Task ListFolders_ReturnsAtLeastInbox()
    {
        if (!IsLiveTestEnabled) return;

        MailService service = CreateMailService(readOnly: true);
        IReadOnlyList<MailFolder> folders = await service.ListFoldersAsync(CancellationToken.None);

        Assert.NotEmpty(folders);
        Assert.Contains(folders, f => f.DisplayName.Equals("Inbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendMessage_ToSelf_Succeeds()
    {
        if (!IsLiveTestEnabled) return;

        MailService service = CreateMailService();

        // Send a test message to self — requires knowing the user's email.
        // We use a search after send to verify. The send itself is the key test.
        // Mail send is skipped by default to avoid unintended mail delivery.
        // To test, replace the email below and uncomment.
        // var request = new MailSendRequest(
        //     To: ["your-test-email@example.com"],
        //     Cc: null, Bcc: null,
        //     Subject: $"Integration Test {Guid.NewGuid():N}",
        //     Body: "Automated integration test message");
        // await service.SendMessageAsync(request, CancellationToken.None);
    }
}
