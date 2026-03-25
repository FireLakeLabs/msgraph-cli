namespace FireLakeLabs.MsGraphCli.Core.Models;

public record DriveItemSummary(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? LastModified, bool IsFolder, string? WebUrl);

public record DriveItemDetail(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    bool IsFolder, string? WebUrl, string? ParentPath,
    string? DownloadUrl);
