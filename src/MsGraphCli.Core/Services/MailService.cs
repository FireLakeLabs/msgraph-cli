using Microsoft.Graph;
using Microsoft.Graph.Models;
using MsGraphCli.Core.Models;

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

    private static IReadOnlyList<MailMessageSummary> MapMessages(List<Message>? messages)
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
