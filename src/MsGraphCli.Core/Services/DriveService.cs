using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using MsGraphCli.Core.Models;

namespace MsGraphCli.Core.Services;

public interface IDriveService
{
    Task<IReadOnlyList<DriveItemSummary>> ListChildrenAsync(string? folderId, string? path, int? max, CancellationToken ct);
    Task<IReadOnlyList<DriveItemSummary>> SearchAsync(string query, int? max, CancellationToken ct);
    Task<DriveItemDetail> GetItemAsync(string itemId, CancellationToken ct);
    Task<DriveItemSummary> DownloadAsync(string? itemId, string? path, string outputPath, CancellationToken ct);
    Task<DriveItemSummary> UploadAsync(string localPath, string remotePath, CancellationToken ct);
    Task<DriveItemSummary> CreateFolderAsync(string name, string? parentPath, CancellationToken ct);
    Task<DriveItemSummary> MoveAsync(string itemId, string destinationPath, CancellationToken ct);
    Task<DriveItemSummary> RenameAsync(string itemId, string newName, CancellationToken ct);
    Task DeleteAsync(string itemId, CancellationToken ct);
}

public sealed class DriveService : IDriveService
{
    private readonly GraphServiceClient _client;
    private string? _driveId;

    private const int MaxSmallFileSize = 4 * 1024 * 1024;

    public DriveService(GraphServiceClient client)
    {
        _client = client;
    }

    private async Task<string> GetDriveIdAsync(CancellationToken ct)
    {
        if (_driveId is null)
        {
            Microsoft.Graph.Models.Drive? drive = await _client.Me.Drive.GetAsync(cancellationToken: ct);
            _driveId = drive?.Id ?? throw new Exceptions.MsGraphCliException(
                "Could not retrieve user's drive.", "DriveNotFound", exitCode: 1);
        }
        return _driveId;
    }

    public async Task<IReadOnlyList<DriveItemSummary>> ListChildrenAsync(
        string? folderId, string? path, int? max, CancellationToken ct)
    {
        int top = max ?? 50;
        string driveId = await GetDriveIdAsync(ct);

        DriveItemCollectionResponse? response;

        if (!string.IsNullOrEmpty(folderId))
        {
            response = await _client.Drives[driveId].Items[folderId].Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                }, ct);
        }
        else if (!string.IsNullOrEmpty(path))
        {
            response = await _client.Drives[driveId].Root.ItemWithPath(path).Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                }, ct);
        }
        else
        {
            response = await _client.Drives[driveId].Items["root"].Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                }, ct);
        }

        return MapSummaries(response?.Value);
    }

    public async Task<IReadOnlyList<DriveItemSummary>> SearchAsync(string query, int? max, CancellationToken ct)
    {
        int top = max ?? 25;
        string driveId = await GetDriveIdAsync(ct);

        var response = await _client.Drives[driveId].SearchWithQ(query)
            .GetAsSearchWithQGetResponseAsync(config =>
            {
                config.QueryParameters.Top = top;
            }, ct);

        return MapSummaries(response?.Value);
    }

    public async Task<DriveItemDetail> GetItemAsync(string itemId, CancellationToken ct)
    {
        string driveId = await GetDriveIdAsync(ct);
        DriveItem? item = await _client.Drives[driveId].Items[itemId].GetAsync(cancellationToken: ct);

        if (item is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Drive item '{itemId}' not found.");
        }

        return MapDetail(item);
    }

    public async Task<DriveItemSummary> DownloadAsync(string? itemId, string? path, string outputPath, CancellationToken ct)
    {
        bool hasId = !string.IsNullOrEmpty(itemId);
        bool hasPath = !string.IsNullOrEmpty(path);

        if (hasId == hasPath)
        {
            throw new Exceptions.MsGraphCliException(
                "Exactly one of itemId or path must be provided.",
                "InvalidArgument", exitCode: 1);
        }

        string driveId = await GetDriveIdAsync(ct);
        DriveItem? item;
        Stream? content;

        if (hasId)
        {
            item = await _client.Drives[driveId].Items[itemId!].GetAsync(cancellationToken: ct);
            if (item is null)
            {
                throw new Exceptions.ResourceNotFoundException($"Drive item '{itemId}' not found.");
            }
            content = await _client.Drives[driveId].Items[itemId!].Content.GetAsync(cancellationToken: ct);
        }
        else
        {
            item = await _client.Drives[driveId].Root.ItemWithPath(path!).GetAsync(cancellationToken: ct);
            if (item is null)
            {
                throw new Exceptions.ResourceNotFoundException($"Drive item at path '{path}' not found.");
            }
            content = await _client.Drives[driveId].Root.ItemWithPath(path!).Content.GetAsync(cancellationToken: ct);
        }

        if (content is null)
        {
            throw new Exceptions.MsGraphCliException(
                "Failed to retrieve file content.",
                "DownloadFailed", exitCode: 1);
        }

        using (content)
        {
            await using FileStream fileStream = File.Create(outputPath);
            await content.CopyToAsync(fileStream, ct);
        }

        return MapSummary(item);
    }

    public async Task<DriveItemSummary> UploadAsync(string localPath, string remotePath, CancellationToken ct)
    {
        long fileSize = new FileInfo(localPath).Length;
        string driveId = await GetDriveIdAsync(ct);

        if (fileSize <= MaxSmallFileSize)
        {
            await using FileStream fileStream = File.OpenRead(localPath);
            DriveItem? uploaded = await _client.Drives[driveId].Root.ItemWithPath(remotePath).Content
                .PutAsync(fileStream, cancellationToken: ct);

            if (uploaded is null)
            {
                throw new Exceptions.MsGraphCliException(
                    $"Failed to upload file to '{remotePath}'.",
                    "UploadFailed", exitCode: 1);
            }

            return MapSummary(uploaded);
        }
        else
        {
            var requestBody = new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "rename" },
                    },
                },
            };

            UploadSession? uploadSession = await _client.Drives[driveId].Root.ItemWithPath(remotePath)
                .CreateUploadSession.PostAsync(requestBody, cancellationToken: ct);

            await using FileStream fileStream = File.OpenRead(localPath);
            LargeFileUploadTask<DriveItem> uploadTask = new(uploadSession, fileStream, 320 * 1024);
            UploadResult<DriveItem> uploadResult = await uploadTask.UploadAsync(cancellationToken: ct);

            DriveItem? resultItem = uploadResult.ItemResponse;

            if (resultItem is null)
            {
                throw new Exceptions.MsGraphCliException(
                    $"Failed to upload large file to '{remotePath}'.",
                    "UploadFailed", exitCode: 1);
            }

            return MapSummary(resultItem);
        }
    }

    public async Task<DriveItemSummary> CreateFolderAsync(string name, string? parentPath, CancellationToken ct)
    {
        string driveId = await GetDriveIdAsync(ct);
        DriveItem? folder;

        if (!string.IsNullOrEmpty(parentPath))
        {
            folder = await _client.Drives[driveId].Root.ItemWithPath(parentPath).Children.PostAsync(new DriveItem
            {
                Name = name,
                Folder = new Folder(),
            }, cancellationToken: ct);
        }
        else
        {
            folder = await _client.Drives[driveId].Items["root"].Children.PostAsync(new DriveItem
            {
                Name = name,
                Folder = new Folder(),
            }, cancellationToken: ct);
        }

        if (folder is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to create folder '{name}'.",
                "CreateFailed", exitCode: 1);
        }

        return MapSummary(folder);
    }

    public async Task<DriveItemSummary> MoveAsync(string itemId, string destinationPath, CancellationToken ct)
    {
        string driveId = await GetDriveIdAsync(ct);
        DriveItem? dest = await _client.Drives[driveId].Root.ItemWithPath(destinationPath).GetAsync(cancellationToken: ct);

        if (dest is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Destination path '{destinationPath}' not found.");
        }

        DriveItem? moved = await _client.Drives[driveId].Items[itemId].PatchAsync(new DriveItem
        {
            ParentReference = new ItemReference { Id = dest.Id },
        }, cancellationToken: ct);

        if (moved is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to move item '{itemId}'.",
                "MoveFailed", exitCode: 1);
        }

        return MapSummary(moved);
    }

    public async Task<DriveItemSummary> RenameAsync(string itemId, string newName, CancellationToken ct)
    {
        string driveId = await GetDriveIdAsync(ct);
        DriveItem? renamed = await _client.Drives[driveId].Items[itemId].PatchAsync(new DriveItem
        {
            Name = newName,
        }, cancellationToken: ct);

        if (renamed is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to rename item '{itemId}'.",
                "RenameFailed", exitCode: 1);
        }

        return MapSummary(renamed);
    }

    public async Task DeleteAsync(string itemId, CancellationToken ct)
    {
        string driveId = await GetDriveIdAsync(ct);
        await _client.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: ct);
    }

    // ── Mapping helpers ──

    private static DriveItemSummary MapSummary(DriveItem item) => new(
        Id: item.Id ?? "",
        Name: item.Name ?? "",
        MimeType: item.File?.MimeType,
        Size: item.Size,
        LastModified: item.LastModifiedDateTime,
        IsFolder: item.Folder is not null,
        WebUrl: item.WebUrl);

    private static DriveItemDetail MapDetail(DriveItem item) => new(
        Id: item.Id ?? "",
        Name: item.Name ?? "",
        MimeType: item.File?.MimeType,
        Size: item.Size,
        Created: item.CreatedDateTime,
        LastModified: item.LastModifiedDateTime,
        IsFolder: item.Folder is not null,
        WebUrl: item.WebUrl,
        ParentPath: item.ParentReference?.Path,
        DownloadUrl: item.AdditionalData != null && item.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out object? url)
            ? url?.ToString()
            : null);

    private static List<DriveItemSummary> MapSummaries(List<DriveItem>? items)
    {
        if (items is null) return [];
        return items.Select(MapSummary).ToList();
    }
}
