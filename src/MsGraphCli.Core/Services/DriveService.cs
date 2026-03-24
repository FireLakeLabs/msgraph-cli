using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using MsGraphCli.Core.Models;

namespace MsGraphCli.Core.Services;

public interface IDriveService
{
    Task<IReadOnlyList<DriveItemSummary>> ListChildrenAsync(string? folderId, string? path, int? max, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriveItemSummary>> SearchAsync(string query, int? max, CancellationToken cancellationToken);
    Task<DriveItemDetail> GetItemAsync(string itemId, CancellationToken cancellationToken);
    Task<DriveItemSummary> DownloadAsync(string? itemId, string? path, string outputPath, CancellationToken cancellationToken);
    Task<DriveItemSummary> UploadAsync(string localPath, string remotePath, CancellationToken cancellationToken);
    Task<DriveItemSummary> CreateFolderAsync(string name, string? parentPath, CancellationToken cancellationToken);
    Task<DriveItemSummary> MoveAsync(string itemId, string destinationPath, CancellationToken cancellationToken);
    Task<DriveItemSummary> RenameAsync(string itemId, string newName, CancellationToken cancellationToken);
    Task DeleteAsync(string itemId, CancellationToken cancellationToken);
}

public sealed class DriveService : IDriveService
{
    private readonly GraphServiceClient _client;
    private string? _driveId;

    private const int MaxSmallFileSize = 4 * 1024 * 1024;

    private static readonly string[] ItemSelect = ["id", "name", "file", "size", "lastModifiedDateTime", "folder", "webUrl"];
    private static readonly string[] ItemDetailSelect = ["id", "name", "file", "size", "createdDateTime", "lastModifiedDateTime", "folder", "webUrl", "parentReference"];

    public DriveService(GraphServiceClient client)
    {
        _client = client;
    }

    private async Task<string> GetDriveIdAsync(CancellationToken cancellationToken)
    {
        if (_driveId is null)
        {
            Microsoft.Graph.Models.Drive? drive = await _client.Me.Drive.GetAsync(cancellationToken: cancellationToken);
            _driveId = drive?.Id ?? throw new Exceptions.MsGraphCliException(
                "Could not retrieve user's drive.", "DriveNotFound", exitCode: 1);
        }
        return _driveId;
    }

    public async Task<IReadOnlyList<DriveItemSummary>> ListChildrenAsync(
        string? folderId, string? path, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 50;
        string driveId = await GetDriveIdAsync(cancellationToken);

        DriveItemCollectionResponse? response;

        if (!string.IsNullOrEmpty(folderId))
        {
            response = await _client.Drives[driveId].Items[folderId].Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = ItemSelect;
                }, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(path))
        {
            response = await _client.Drives[driveId].Root.ItemWithPath(path).Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = ItemSelect;
                }, cancellationToken);
        }
        else
        {
            response = await _client.Drives[driveId].Items["root"].Children
                .GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = ItemSelect;
                }, cancellationToken);
        }

        return MapSummaries(response?.Value);
    }

    public async Task<IReadOnlyList<DriveItemSummary>> SearchAsync(string query, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 25;
        string driveId = await GetDriveIdAsync(cancellationToken);

        var response = await _client.Drives[driveId].SearchWithQ(query)
            .GetAsSearchWithQGetResponseAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = ItemSelect;
            }, cancellationToken);

        return MapSummaries(response?.Value);
    }

    public async Task<DriveItemDetail> GetItemAsync(string itemId, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);
        DriveItem? item = await _client.Drives[driveId].Items[itemId].GetAsync(config =>
        {
            config.QueryParameters.Select = ItemDetailSelect;
        }, cancellationToken);

        if (item is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Drive item '{itemId}' not found.");
        }

        return MapDetail(item);
    }

    public async Task<DriveItemSummary> DownloadAsync(string? itemId, string? path, string outputPath, CancellationToken cancellationToken)
    {
        bool hasId = !string.IsNullOrEmpty(itemId);
        bool hasPath = !string.IsNullOrEmpty(path);

        if (hasId == hasPath)
        {
            throw new Exceptions.MsGraphCliException(
                "Exactly one of itemId or path must be provided.",
                "InvalidArgument", exitCode: 1);
        }

        string driveId = await GetDriveIdAsync(cancellationToken);
        DriveItem? item;
        Stream? content;

        if (hasId)
        {
            item = await _client.Drives[driveId].Items[itemId!].GetAsync(cancellationToken: cancellationToken);
            if (item is null)
            {
                throw new Exceptions.ResourceNotFoundException($"Drive item '{itemId}' not found.");
            }
            content = await _client.Drives[driveId].Items[itemId!].Content.GetAsync(cancellationToken: cancellationToken);
        }
        else
        {
            item = await _client.Drives[driveId].Root.ItemWithPath(path!).GetAsync(cancellationToken: cancellationToken);
            if (item is null)
            {
                throw new Exceptions.ResourceNotFoundException($"Drive item at path '{path}' not found.");
            }
            content = await _client.Drives[driveId].Root.ItemWithPath(path!).Content.GetAsync(cancellationToken: cancellationToken);
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
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        return MapSummary(item);
    }

    public async Task<DriveItemSummary> UploadAsync(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        long fileSize = new FileInfo(localPath).Length;
        string driveId = await GetDriveIdAsync(cancellationToken);

        if (fileSize <= MaxSmallFileSize)
        {
            await using FileStream fileStream = File.OpenRead(localPath);
            DriveItem? uploaded = await _client.Drives[driveId].Root.ItemWithPath(remotePath).Content
                .PutAsync(fileStream, cancellationToken: cancellationToken);

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
                .CreateUploadSession.PostAsync(requestBody, cancellationToken: cancellationToken);

            if (uploadSession is null)
            {
                throw new Exceptions.MsGraphCliException(
                    $"Failed to create upload session for '{remotePath}'.",
                    "UploadSessionFailed", exitCode: 1);
            }

            await using FileStream fileStream = File.OpenRead(localPath);
            LargeFileUploadTask<DriveItem> uploadTask = new(uploadSession, fileStream, 320 * 1024);
            UploadResult<DriveItem> uploadResult = await uploadTask.UploadAsync(cancellationToken: cancellationToken);

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

    public async Task<DriveItemSummary> CreateFolderAsync(string name, string? parentPath, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);
        DriveItem? folder;

        if (!string.IsNullOrEmpty(parentPath))
        {
            folder = await _client.Drives[driveId].Root.ItemWithPath(parentPath).Children.PostAsync(new DriveItem
            {
                Name = name,
                Folder = new Folder(),
            }, cancellationToken: cancellationToken);
        }
        else
        {
            folder = await _client.Drives[driveId].Items["root"].Children.PostAsync(new DriveItem
            {
                Name = name,
                Folder = new Folder(),
            }, cancellationToken: cancellationToken);
        }

        if (folder is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to create folder '{name}'.",
                "CreateFailed", exitCode: 1);
        }

        return MapSummary(folder);
    }

    public async Task<DriveItemSummary> MoveAsync(string itemId, string destinationPath, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        DriveItem? dest = destinationPath is "/" or ""
            ? await _client.Drives[driveId].Items["root"].GetAsync(cancellationToken: cancellationToken)
            : await _client.Drives[driveId].Root.ItemWithPath(destinationPath).GetAsync(cancellationToken: cancellationToken);

        if (dest is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Destination path '{destinationPath}' not found.");
        }

        DriveItem? moved = await _client.Drives[driveId].Items[itemId].PatchAsync(new DriveItem
        {
            ParentReference = new ItemReference { Id = dest.Id },
        }, cancellationToken: cancellationToken);

        if (moved is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to move item '{itemId}'.",
                "MoveFailed", exitCode: 1);
        }

        return MapSummary(moved);
    }

    public async Task<DriveItemSummary> RenameAsync(string itemId, string newName, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);
        DriveItem? renamed = await _client.Drives[driveId].Items[itemId].PatchAsync(new DriveItem
        {
            Name = newName,
        }, cancellationToken: cancellationToken);

        if (renamed is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to rename item '{itemId}'.",
                "RenameFailed", exitCode: 1);
        }

        return MapSummary(renamed);
    }

    public async Task DeleteAsync(string itemId, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);
        await _client.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: cancellationToken);
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
