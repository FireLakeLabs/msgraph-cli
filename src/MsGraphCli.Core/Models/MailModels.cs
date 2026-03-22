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
