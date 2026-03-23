using Microsoft.Graph;
using Microsoft.Graph.Models;
using MsGraphCli.Core.Models;
using MailFolder = MsGraphCli.Core.Models.MailFolder;

namespace MsGraphCli.Core.Services;

public interface IMailService
{
    Task<IReadOnlyList<MailMessageSummary>> ListMessagesAsync(
        string? folderId, int? max, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailMessageSummary>> SearchMessagesAsync(
        string query, int? max, CancellationToken cancellationToken);

    Task<MailMessageDetail> GetMessageAsync(
        string messageId, bool includeBody, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken cancellationToken);

    Task SendMessageAsync(MailSendRequest request, CancellationToken cancellationToken);

    Task ReplyAsync(string messageId, string body, bool replyAll, CancellationToken cancellationToken);

    Task ForwardAsync(string messageId, IReadOnlyList<string> toRecipients, string? body, CancellationToken cancellationToken);

    Task<MailMoveResult> MoveMessageAsync(string messageId, string destinationFolderId, CancellationToken cancellationToken);

    Task SetReadStatusAsync(string messageId, bool isRead, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailAttachmentInfo>> ListAttachmentsAsync(string messageId, CancellationToken cancellationToken);

    Task<(byte[] Content, string FileName, string ContentType)> DownloadAttachmentAsync(
        string messageId, string attachmentId, CancellationToken cancellationToken);
}

public sealed class MailService : IMailService
{
    private readonly GraphServiceClient _client;

    private static readonly string[] SummarySelect =
        ["id", "from", "subject", "receivedDateTime", "isRead", "hasAttachments", "bodyPreview"];

    private static readonly string[] DetailSelect =
        ["id", "from", "toRecipients", "ccRecipients", "subject", "receivedDateTime", "isRead", "hasAttachments", "body"];

    private static readonly string[] DetailSelectNoBody =
        ["id", "from", "toRecipients", "ccRecipients", "subject", "receivedDateTime", "isRead", "hasAttachments"];

    public MailService(GraphServiceClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<MailMessageSummary>> ListMessagesAsync(
        string? folderId, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 25;

        MessageCollectionResponse? response;

        if (!string.IsNullOrEmpty(folderId))
        {
            response = await _client.Me.MailFolders[folderId].Messages
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = SummarySelect;
                    config.QueryParameters.Orderby = ["receivedDateTime desc"];
                }, cancellationToken);
        }
        else
        {
            response = await _client.Me.Messages
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = SummarySelect;
                    config.QueryParameters.Orderby = ["receivedDateTime desc"];
                }, cancellationToken);
        }

        return MapMessages(response?.Value);
    }

    public async Task<IReadOnlyList<MailMessageSummary>> SearchMessagesAsync(
        string query, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 25;

        var response = await _client.Me.Messages
            .GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = SummarySelect;
                config.QueryParameters.Search = $"\"{query}\"";
                config.Headers.Add("ConsistencyLevel", "eventual");
                config.QueryParameters.Count = true;
            }, cancellationToken);

        return MapMessages(response?.Value);
    }

    public async Task<MailMessageDetail> GetMessageAsync(
        string messageId, bool includeBody, CancellationToken cancellationToken)
    {
        string[] select = includeBody ? DetailSelect : DetailSelectNoBody;

        Message? message = await _client.Me.Messages[messageId]
            .GetAsync(config =>
            {
                config.QueryParameters.Select = select;
            }, cancellationToken);

        if (message is null)
        {
            throw new InvalidOperationException($"Message '{messageId}' not found.");
        }

        return new MailMessageDetail(
            Id: message.Id ?? "",
            From: message.From?.EmailAddress?.Address ?? "(unknown)",
            ToRecipients: message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
            CcRecipients: message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
            Subject: message.Subject ?? "(no subject)",
            ReceivedDateTime: message.ReceivedDateTime ?? DateTimeOffset.MinValue,
            IsRead: message.IsRead ?? false,
            HasAttachments: message.HasAttachments ?? false,
            BodyText: includeBody ? message.Body?.Content : null,
            BodyHtml: includeBody && message.Body?.ContentType == BodyType.Html ? message.Body?.Content : null
        );
    }

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        var response = await _client.Me.MailFolders
            .GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Select = ["id", "displayName", "totalItemCount", "unreadItemCount"];
            }, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(f => new MailFolder(
            Id: f.Id ?? "",
            DisplayName: f.DisplayName ?? "",
            TotalItemCount: f.TotalItemCount ?? 0,
            UnreadItemCount: f.UnreadItemCount ?? 0
        )).ToList();
    }

    public async Task SendMessageAsync(MailSendRequest request, CancellationToken cancellationToken)
    {
        var message = new Message
        {
            Subject = request.Subject,
            Body = new ItemBody
            {
                ContentType = string.Equals(request.BodyContentType, "HTML", StringComparison.OrdinalIgnoreCase)
                    ? BodyType.Html : BodyType.Text,
                Content = request.Body,
            },
            ToRecipients = request.To.Select(ToRecipient).ToList(),
        };

        if (request.Cc is { Count: > 0 })
        {
            message.CcRecipients = request.Cc.Select(ToRecipient).ToList();
        }

        if (request.Bcc is { Count: > 0 })
        {
            message.BccRecipients = request.Bcc.Select(ToRecipient).ToList();
        }

        if (request.AttachmentPaths is { Count: > 0 })
        {
            message.Attachments = [];
            foreach (string path in request.AttachmentPaths)
            {
                byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
                if (content.Length > 3 * 1024 * 1024)
                {
                    throw new InvalidOperationException(
                        $"Attachment '{Path.GetFileName(path)}' exceeds 3 MB limit. Large file upload is not yet supported.");
                }

                message.Attachments.Add(new FileAttachment
                {
                    Name = Path.GetFileName(path),
                    ContentBytes = content,
                });
            }
        }

        await _client.Me.SendMail.PostAsync(
            new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true,
            }, cancellationToken: cancellationToken);
    }

    public async Task ReplyAsync(string messageId, string body, bool replyAll, CancellationToken cancellationToken)
    {
        if (replyAll)
        {
            await _client.Me.Messages[messageId].ReplyAll.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.ReplyAll.ReplyAllPostRequestBody
                {
                    Comment = body,
                }, cancellationToken: cancellationToken);
        }
        else
        {
            await _client.Me.Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Comment = body,
                }, cancellationToken: cancellationToken);
        }
    }

    public async Task ForwardAsync(string messageId, IReadOnlyList<string> toRecipients, string? body, CancellationToken cancellationToken)
    {
        await _client.Me.Messages[messageId].Forward.PostAsync(
            new Microsoft.Graph.Me.Messages.Item.Forward.ForwardPostRequestBody
            {
                Comment = body,
                ToRecipients = toRecipients.Select(ToRecipient).ToList(),
            }, cancellationToken: cancellationToken);
    }

    public async Task<MailMoveResult> MoveMessageAsync(string messageId, string destinationFolderId, CancellationToken cancellationToken)
    {
        Message? moved = await _client.Me.Messages[messageId].Move.PostAsync(
            new Microsoft.Graph.Me.Messages.Item.Move.MovePostRequestBody
            {
                DestinationId = destinationFolderId,
            }, cancellationToken: cancellationToken);

        return new MailMoveResult(moved?.Id ?? messageId, destinationFolderId);
    }

    public async Task SetReadStatusAsync(string messageId, bool isRead, CancellationToken cancellationToken)
    {
        await _client.Me.Messages[messageId].PatchAsync(new Message
        {
            IsRead = isRead,
        }, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<MailAttachmentInfo>> ListAttachmentsAsync(string messageId, CancellationToken cancellationToken)
    {
        AttachmentCollectionResponse? response = await _client.Me.Messages[messageId].Attachments
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "name", "contentType", "size"];
            }, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(a => new MailAttachmentInfo(
            Id: a.Id ?? "",
            Name: a.Name ?? "",
            ContentType: a.ContentType ?? "",
            Size: a.Size ?? 0
        )).ToList();
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> DownloadAttachmentAsync(
        string messageId, string attachmentId, CancellationToken cancellationToken)
    {
        Attachment? attachment = await _client.Me.Messages[messageId].Attachments[attachmentId]
            .GetAsync(cancellationToken: cancellationToken);

        if (attachment is FileAttachment fileAttachment)
        {
            return (
                fileAttachment.ContentBytes ?? [],
                fileAttachment.Name ?? "attachment",
                fileAttachment.ContentType ?? "application/octet-stream"
            );
        }

        throw new InvalidOperationException($"Attachment '{attachmentId}' is not a file attachment.");
    }

    private static Recipient ToRecipient(string email) => new()
    {
        EmailAddress = new EmailAddress { Address = email },
    };

    private static List<MailMessageSummary> MapMessages(List<Message>? messages)
    {
        if (messages is null)
        {
            return [];
        }

        return messages.Select(m => new MailMessageSummary(
            Id: m.Id ?? "",
            From: m.From?.EmailAddress?.Address ?? "(unknown)",
            Subject: m.Subject ?? "(no subject)",
            ReceivedDateTime: m.ReceivedDateTime ?? DateTimeOffset.MinValue,
            IsRead: m.IsRead ?? false,
            HasAttachments: m.HasAttachments ?? false,
            Preview: m.BodyPreview
        )).ToList();
    }
}
