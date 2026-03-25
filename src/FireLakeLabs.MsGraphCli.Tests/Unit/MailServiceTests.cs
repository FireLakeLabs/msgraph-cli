using System.Net;
using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Core.Models;
using FireLakeLabs.MsGraphCli.Core.Services;
using FireLakeLabs.MsGraphCli.Tests.Unit.Helpers;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class MailServiceTests
{
    // ── ListMessagesAsync ──

    [Fact]
    public async Task ListMessages_NoFolder_CallsMessagesEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.ListMessagesAsync(null, null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/me/messages");
        handler.LastRequest.Uri.AbsolutePath.Should().NotContain("mailFolders");
    }

    [Fact]
    public async Task ListMessages_WithFolder_CallsFolderMessagesEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.ListMessagesAsync("inbox-id", null, CancellationToken.None);

        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/mailFolders/inbox-id/messages");
    }

    [Fact]
    public async Task ListMessages_DefaultMax_Uses25()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.ListMessagesAsync(null, null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=25");
    }

    [Fact]
    public async Task ListMessages_CustomMax_UsesProvidedValue()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.ListMessagesAsync(null, 10, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=10");
    }

    [Fact]
    public async Task ListMessages_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        MailService svc = CreateService(handler);

        IReadOnlyList<MailMessageSummary> result =
            await svc.ListMessagesAsync(null, null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListMessages_MapsFieldsCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [{
                "id": "msg-1",
                "subject": "Test Subject",
                "from": { "emailAddress": { "address": "sender@example.com" } },
                "receivedDateTime": "2025-01-15T10:30:00Z",
                "isRead": true,
                "hasAttachments": false,
                "bodyPreview": "Preview text"
            }]
        }
        """);
        MailService svc = CreateService(handler);

        IReadOnlyList<MailMessageSummary> result =
            await svc.ListMessagesAsync(null, null, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("msg-1");
        result[0].From.Should().Be("sender@example.com");
        result[0].Subject.Should().Be("Test Subject");
        result[0].IsRead.Should().BeTrue();
        result[0].HasAttachments.Should().BeFalse();
        result[0].Preview.Should().Be("Preview text");
    }

    // ── SearchMessagesAsync ──

    [Fact]
    public async Task SearchMessages_SetsSearchQuery()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.SearchMessagesAsync("budget report", null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("$search=\"budget report\"");
    }

    [Fact]
    public async Task SearchMessages_SetsConsistencyLevelHeader()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.SearchMessagesAsync("test", null, CancellationToken.None);

        handler.LastRequest.Headers.Should().Contain(h =>
            h.Key == "ConsistencyLevel" && h.Value.Contains("eventual"));
    }

    [Fact]
    public async Task SearchMessages_DefaultMax_Uses25()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        MailService svc = CreateService(handler);

        await svc.SearchMessagesAsync("test", null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=25");
    }

    // ── GetMessageAsync ──

    [Fact]
    public async Task GetMessage_IncludeBody_SelectsBodyField()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(MessageJson("msg-1"));
        MailService svc = CreateService(handler);

        await svc.GetMessageAsync("msg-1", includeBody: true, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("body");
    }

    [Fact]
    public async Task GetMessage_ExcludeBody_OmitsBodyField()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue(MessageJson("msg-1"));
        MailService svc = CreateService(handler);

        await svc.GetMessageAsync("msg-1", includeBody: false, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().NotContain("body");
    }

    [Fact]
    public async Task GetMessage_MapsDetailFieldsCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "id": "msg-1",
            "subject": "Detail Test",
            "from": { "emailAddress": { "address": "from@test.com" } },
            "toRecipients": [{ "emailAddress": { "address": "to@test.com" } }],
            "ccRecipients": [{ "emailAddress": { "address": "cc@test.com" } }],
            "receivedDateTime": "2025-01-15T10:30:00Z",
            "isRead": false,
            "hasAttachments": true,
            "body": { "contentType": "text", "content": "Hello world" }
        }
        """);
        MailService svc = CreateService(handler);

        MailMessageDetail detail = await svc.GetMessageAsync("msg-1", includeBody: true, CancellationToken.None);

        detail.Id.Should().Be("msg-1");
        detail.Subject.Should().Be("Detail Test");
        detail.From.Should().Be("from@test.com");
        detail.ToRecipients.Should().Contain("to@test.com");
        detail.CcRecipients.Should().Contain("cc@test.com");
        detail.IsRead.Should().BeFalse();
        detail.HasAttachments.Should().BeTrue();
        detail.BodyText.Should().Be("Hello world");
        detail.BodyHtml.Should().BeNull();
    }

    [Fact]
    public async Task GetMessage_HtmlBody_SetsBodyHtml()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "id": "msg-1",
            "subject": "HTML Test",
            "from": { "emailAddress": { "address": "from@test.com" } },
            "receivedDateTime": "2025-01-15T10:30:00Z",
            "isRead": false,
            "hasAttachments": false,
            "body": { "contentType": "html", "content": "<p>Rich</p>" }
        }
        """);
        MailService svc = CreateService(handler);

        MailMessageDetail detail = await svc.GetMessageAsync("msg-1", includeBody: true, CancellationToken.None);

        detail.BodyText.Should().Be("<p>Rich</p>");
        detail.BodyHtml.Should().Be("<p>Rich</p>");
    }

    // ── ListFoldersAsync ──

    [Fact]
    public async Task ListFolders_ReturnsFolders()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [
                { "id": "folder-1", "displayName": "Inbox", "totalItemCount": 42, "unreadItemCount": 5 },
                { "id": "folder-2", "displayName": "Sent", "totalItemCount": 10, "unreadItemCount": 0 }
            ]
        }
        """);
        MailService svc = CreateService(handler);

        IReadOnlyList<MailFolder> result = await svc.ListFoldersAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].DisplayName.Should().Be("Inbox");
        result[0].TotalItemCount.Should().Be(42);
        result[0].UnreadItemCount.Should().Be(5);
    }

    [Fact]
    public async Task ListFolders_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        MailService svc = CreateService(handler);

        IReadOnlyList<MailFolder> result = await svc.ListFoldersAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── SendMessageAsync ──

    [Fact]
    public async Task SendMessage_BasicTextEmail_PostsToSendMail()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        var request = new MailSendRequest(
            To: ["recipient@example.com"],
            Cc: null, Bcc: null,
            Subject: "Test Subject",
            Body: "Plain text body");

        await svc.SendMessageAsync(request, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/sendMail");
        handler.LastRequest.Body.Should().Contain("Test Subject");
        handler.LastRequest.Body.Should().Contain("recipient@example.com");
    }

    [Fact]
    public async Task SendMessage_HtmlBody_SetsHtmlContentType()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        var request = new MailSendRequest(
            To: ["r@test.com"], Cc: null, Bcc: null,
            Subject: "HTML", Body: "<p>HTML</p>",
            BodyContentType: "HTML");

        await svc.SendMessageAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("html");
    }

    [Fact]
    public async Task SendMessage_HtmlBodyCaseInsensitive_SetsHtmlContentType()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        var request = new MailSendRequest(
            To: ["r@test.com"], Cc: null, Bcc: null,
            Subject: "HTML", Body: "<p>HTML</p>",
            BodyContentType: "html");

        await svc.SendMessageAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("html");
    }

    [Fact]
    public async Task SendMessage_WithCc_IncludesCcRecipients()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        var request = new MailSendRequest(
            To: ["to@test.com"],
            Cc: ["cc@test.com"],
            Bcc: null,
            Subject: "CC Test", Body: "body");

        await svc.SendMessageAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("cc@test.com");
    }

    [Fact]
    public async Task SendMessage_WithBcc_IncludesBccRecipients()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        var request = new MailSendRequest(
            To: ["to@test.com"], Cc: null,
            Bcc: ["bcc@test.com"],
            Subject: "BCC Test", Body: "body");

        await svc.SendMessageAsync(request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("bcc@test.com");
    }

    [Fact]
    public async Task SendMessage_WithAttachment_IncludesFileAttachment()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, [1, 2, 3, 4, 5]);

            var handler = new MockGraphHandler();
            handler.EnqueueEmpty();
            MailService svc = CreateService(handler);

            var request = new MailSendRequest(
                To: ["to@test.com"], Cc: null, Bcc: null,
                Subject: "Attach", Body: "See attached",
                AttachmentPaths: [tempFile]);

            await svc.SendMessageAsync(request, CancellationToken.None);

            handler.LastRequest.Body.Should().Contain(Path.GetFileName(tempFile));
            handler.LastRequest.Body.Should().Contain("contentBytes");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SendMessage_AttachmentExceeds3MB_ThrowsMsGraphCliException()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            byte[] largeContent = new byte[3 * 1024 * 1024 + 1];
            await File.WriteAllBytesAsync(tempFile, largeContent);

            var handler = new MockGraphHandler();
            handler.EnqueueEmpty();
            MailService svc = CreateService(handler);

            var request = new MailSendRequest(
                To: ["to@test.com"], Cc: null, Bcc: null,
                Subject: "Big", Body: "body",
                AttachmentPaths: [tempFile]);

            Func<Task> act = () => svc.SendMessageAsync(request, CancellationToken.None);

            MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
            ex.ErrorCode.Should().Be("AttachmentTooLarge");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── ReplyAsync ──

    [Fact]
    public async Task Reply_ReplyAllFalse_PostsToReplyEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        await svc.ReplyAsync("msg-1", "Thanks", replyAll: false, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/reply");
    }

    [Fact]
    public async Task Reply_ReplyAllTrue_PostsToReplyAllEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        await svc.ReplyAsync("msg-1", "Acknowledged", replyAll: true, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/replyAll");
    }

    // ── ForwardAsync ──

    [Fact]
    public async Task Forward_PostsToForwardEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        MailService svc = CreateService(handler);

        await svc.ForwardAsync("msg-1", ["fwd@test.com"], "FYI", CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().EndWith("/forward");
        handler.LastRequest.Body.Should().Contain("fwd@test.com");
    }

    // ── MoveMessageAsync ──

    [Fact]
    public async Task MoveMessage_ReturnsResultWithIds()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{ "id": "moved-msg-1" }""");
        MailService svc = CreateService(handler);

        MailMoveResult result = await svc.MoveMessageAsync("msg-1", "archive-folder", CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/move");
        result.MessageId.Should().Be("moved-msg-1");
        result.DestinationFolderId.Should().Be("archive-folder");
    }

    // ── SetReadStatusAsync ──

    [Fact]
    public async Task SetReadStatus_True_PatchesIsRead()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{ "id": "msg-1", "isRead": true }""");
        MailService svc = CreateService(handler);

        await svc.SetReadStatusAsync("msg-1", isRead: true, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Body.Should().Contain("\"isRead\":true");
    }

    [Fact]
    public async Task SetReadStatus_False_PatchesIsRead()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{ "id": "msg-1", "isRead": false }""");
        MailService svc = CreateService(handler);

        await svc.SetReadStatusAsync("msg-1", isRead: false, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Body.Should().Contain("\"isRead\":false");
    }

    // ── ListAttachmentsAsync ──

    [Fact]
    public async Task ListAttachments_ReturnsAttachmentInfos()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [
                { "id": "att-1", "name": "report.pdf", "contentType": "application/pdf", "size": 12345 },
                { "id": "att-2", "name": "image.png", "contentType": "image/png", "size": 67890 }
            ]
        }
        """);
        MailService svc = CreateService(handler);

        IReadOnlyList<MailAttachmentInfo> result =
            await svc.ListAttachmentsAsync("msg-1", CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("att-1");
        result[0].Name.Should().Be("report.pdf");
        result[0].ContentType.Should().Be("application/pdf");
        result[0].Size.Should().Be(12345);
    }

    [Fact]
    public async Task ListAttachments_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        MailService svc = CreateService(handler);

        IReadOnlyList<MailAttachmentInfo> result =
            await svc.ListAttachmentsAsync("msg-1", CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── DownloadAttachmentAsync ──

    [Fact]
    public async Task DownloadAttachment_FileAttachment_ReturnsContentAndMetadata()
    {
        string base64Content = Convert.ToBase64String([72, 101, 108, 108, 111]); // "Hello"
        var handler = new MockGraphHandler();
        handler.Enqueue($$"""
        {
            "@odata.type": "#microsoft.graph.fileAttachment",
            "id": "att-1",
            "name": "hello.txt",
            "contentType": "text/plain",
            "size": 5,
            "contentBytes": "{{base64Content}}"
        }
        """);
        MailService svc = CreateService(handler);

        (byte[] content, string fileName, string contentType) =
            await svc.DownloadAttachmentAsync("msg-1", "att-1", CancellationToken.None);

        content.Should().BeEquivalentTo(new byte[] { 72, 101, 108, 108, 111 });
        fileName.Should().Be("hello.txt");
        contentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task DownloadAttachment_NonFileAttachment_ThrowsMsGraphCliException()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "@odata.type": "#microsoft.graph.itemAttachment",
            "id": "att-1",
            "name": "attached-item"
        }
        """);
        MailService svc = CreateService(handler);

        Func<Task> act = () => svc.DownloadAttachmentAsync("msg-1", "att-1", CancellationToken.None);

        MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
        ex.ErrorCode.Should().Be("UnsupportedAttachmentType");
    }

    // ── Helpers ──

    private static MailService CreateService(MockGraphHandler handler)
    {
        return new MailService(MockGraphHandler.CreateClient(handler));
    }

    private static string MessageJson(string id) => $$"""
    {
        "id": "{{id}}",
        "subject": "Test",
        "from": { "emailAddress": { "address": "test@test.com" } },
        "receivedDateTime": "2025-01-15T10:30:00Z",
        "isRead": false,
        "hasAttachments": false
    }
    """;
}
