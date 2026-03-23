namespace MsGraphCli.Core.Models;

public record MailMessageSummary(
    string Id,
    string From,
    string Subject,
    DateTimeOffset ReceivedDateTime,
    bool IsRead,
    bool HasAttachments,
    string? Preview
);

public record MailMessageDetail(
    string Id,
    string From,
    IReadOnlyList<string> ToRecipients,
    IReadOnlyList<string> CcRecipients,
    string Subject,
    DateTimeOffset ReceivedDateTime,
    bool IsRead,
    bool HasAttachments,
    string? BodyText,
    string? BodyHtml
);

public record MailFolder(
    string Id,
    string DisplayName,
    int TotalItemCount,
    int UnreadItemCount
);

public record MailAttachmentInfo(
    string Id,
    string Name,
    string ContentType,
    long Size
);

public record MailSendRequest(
    IReadOnlyList<string> To,
    IReadOnlyList<string>? Cc,
    IReadOnlyList<string>? Bcc,
    string Subject,
    string Body,
    string BodyContentType = "Text",
    IReadOnlyList<string>? AttachmentPaths = null
);

public record MailMoveResult(
    string MessageId,
    string DestinationFolderId
);
