namespace FireLakeLabs.MsGraphCli.Core.Models;

public record TaskListInfo(string Id, string DisplayName, bool IsDefaultList);

public record TodoTaskItem(
    string Id, string Title, string? Body, string Status,
    DateTimeOffset? DueDate, DateTimeOffset? CompletedDate,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    string Importance);

public record TodoTaskCreateRequest(
    string Title,
    string? Body = null,
    DateTimeOffset? DueDate = null,
    string Importance = "normal");

public record TodoTaskUpdateRequest(
    string? Title = null,
    string? Body = null,
    DateTimeOffset? DueDate = null,
    string? Importance = null);
