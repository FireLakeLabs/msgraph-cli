using System.Net;
using FluentAssertions;
using MsGraphCli.Core.Exceptions;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using MsGraphCli.Tests.Unit.Helpers;
using Xunit;

namespace MsGraphCli.Tests.Unit;

public class DriveServiceTests
{
    // ── ListChildrenAsync ──

    [Fact]
    public async Task ListChildren_NoOptions_CallsRootChildren()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.ListChildrenAsync(null, null, null, CancellationToken.None);

        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/items/root/children");
    }

    [Fact]
    public async Task ListChildren_WithFolderId_CallsItemChildren()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.ListChildrenAsync("folder-123", null, null, CancellationToken.None);

        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/items/folder-123/children");
    }

    [Fact]
    public async Task ListChildren_WithPath_CallsPathChildren()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.ListChildrenAsync(null, "/Documents", null, CancellationToken.None);

        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("/Documents");
        path.Should().Contain("/children");
    }

    [Fact]
    public async Task ListChildren_DefaultMax_Uses50()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.ListChildrenAsync(null, null, null, CancellationToken.None);

        handler.Requests[1].Uri.Query.Should().Contain("%24top=50");
    }

    [Fact]
    public async Task ListChildren_MapsFieldsCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "value": [{
                "id": "item-1",
                "name": "report.docx",
                "file": { "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                "size": 12345,
                "lastModifiedDateTime": "2025-06-15T10:30:00Z",
                "webUrl": "https://onedrive.example.com/report.docx"
            }]
        }
        """);
        DriveService svc = CreateService(handler);

        IReadOnlyList<DriveItemSummary> result =
            await svc.ListChildrenAsync(null, null, null, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("item-1");
        result[0].Name.Should().Be("report.docx");
        result[0].MimeType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result[0].Size.Should().Be(12345);
        result[0].IsFolder.Should().BeFalse();
        result[0].WebUrl.Should().Be("https://onedrive.example.com/report.docx");
    }

    [Fact]
    public async Task ListChildren_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("{}");
        DriveService svc = CreateService(handler);

        IReadOnlyList<DriveItemSummary> result =
            await svc.ListChildrenAsync(null, null, null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── SearchAsync ──

    [Fact]
    public async Task Search_CallsSearchEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.SearchAsync("budget", null, CancellationToken.None);

        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("search");
    }

    [Fact]
    public async Task Search_DefaultMax_Uses25()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        DriveService svc = CreateService(handler);

        await svc.SearchAsync("budget", null, CancellationToken.None);

        handler.Requests[1].Uri.Query.Should().Contain("%24top=25");
    }

    // ── GetItemAsync ──

    [Fact]
    public async Task GetItem_ReturnsDetail()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "id": "item-99",
            "name": "presentation.pptx",
            "file": { "mimeType": "application/vnd.ms-powerpoint" },
            "size": 5000000,
            "createdDateTime": "2025-01-10T08:00:00Z",
            "lastModifiedDateTime": "2025-06-15T10:30:00Z",
            "webUrl": "https://onedrive.example.com/presentation.pptx",
            "parentReference": { "path": "/drive/root:/Documents" },
            "@microsoft.graph.downloadUrl": "https://download.example.com/file"
        }
        """);
        DriveService svc = CreateService(handler);

        DriveItemDetail detail = await svc.GetItemAsync("item-99", CancellationToken.None);

        detail.Id.Should().Be("item-99");
        detail.Name.Should().Be("presentation.pptx");
        detail.MimeType.Should().Be("application/vnd.ms-powerpoint");
        detail.Size.Should().Be(5000000);
        detail.Created.Should().NotBeNull();
        detail.LastModified.Should().NotBeNull();
        detail.IsFolder.Should().BeFalse();
        detail.WebUrl.Should().Be("https://onedrive.example.com/presentation.pptx");
        detail.ParentPath.Should().Be("/drive/root:/Documents");
        detail.DownloadUrl.Should().Be("https://download.example.com/file");
    }

    [Fact]
    public async Task GetItem_NullResponse_ThrowsResourceNotFoundException()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("{}");
        DriveService svc = CreateService(handler);

        Func<Task> act = () => svc.GetItemAsync("nonexistent", CancellationToken.None);

        // The Graph SDK deserializes "{}" into a DriveItem with null Id, which the service
        // still returns (only null response triggers the exception). But we need to test
        // the null-response path. Graph SDK returns null when status is 204/empty.
        // Use an empty response with 204 to trigger null.
        var handler2 = new MockGraphHandler();
        handler2.Enqueue("""{"id": "test-drive-id"}""");
        handler2.EnqueueEmpty();
        DriveService svc2 = CreateService(handler2);

        Func<Task> act2 = () => svc2.GetItemAsync("nonexistent", CancellationToken.None);

        await act2.Should().ThrowAsync<ResourceNotFoundException>();
    }

    // ── DownloadAsync ──

    [Fact]
    public async Task Download_BothItemIdAndPath_ThrowsMsGraphCliException()
    {
        var handler = new MockGraphHandler();
        DriveService svc = CreateService(handler);

        Func<Task> act = () => svc.DownloadAsync("item-1", "/path/file.txt", "/tmp/out", CancellationToken.None);

        MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
        ex.ErrorCode.Should().Be("InvalidArgument");
    }

    [Fact]
    public async Task Download_NeitherItemIdNorPath_ThrowsMsGraphCliException()
    {
        var handler = new MockGraphHandler();
        DriveService svc = CreateService(handler);

        Func<Task> act = () => svc.DownloadAsync(null, null, "/tmp/out", CancellationToken.None);

        MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
        ex.ErrorCode.Should().Be("InvalidArgument");
    }

    // ── UploadAsync ──

    [Fact]
    public async Task Upload_SmallFile_UsesPut()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[100]);

            var handler = new MockGraphHandler();
            handler.Enqueue("""{"id": "test-drive-id"}""");
            handler.Enqueue("""
            {
                "id": "uploaded-1",
                "name": "test.txt",
                "size": 100,
                "lastModifiedDateTime": "2025-06-15T10:30:00Z"
            }
            """);
            DriveService svc = CreateService(handler);

            DriveItemSummary result = await svc.UploadAsync(tempFile, "/test.txt", CancellationToken.None);

            handler.Requests[1].Method.Should().Be(HttpMethod.Put);
            result.Id.Should().Be("uploaded-1");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── CreateFolderAsync ──

    [Fact]
    public async Task CreateFolder_PostsWithFolderProperty()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "id": "new-folder-1",
            "name": "Reports",
            "folder": { "childCount": 0 },
            "lastModifiedDateTime": "2025-06-15T10:30:00Z"
        }
        """);
        DriveService svc = CreateService(handler);

        DriveItemSummary result = await svc.CreateFolderAsync("Reports", null, CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Body.Should().Contain("Reports");
        handler.Requests[1].Body.Should().Contain("folder");
        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/items/root/children");
        result.Name.Should().Be("Reports");
        result.IsFolder.Should().BeTrue();
    }

    [Fact]
    public async Task CreateFolder_WithParentPath_UsesPathEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "id": "new-folder-2",
            "name": "SubFolder",
            "folder": { "childCount": 0 },
            "lastModifiedDateTime": "2025-06-15T10:30:00Z"
        }
        """);
        DriveService svc = CreateService(handler);

        DriveItemSummary result = await svc.CreateFolderAsync("SubFolder", "/Documents", CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("/Documents");
        path.Should().Contain("/children");
        result.Name.Should().Be("SubFolder");
    }

    // ── RenameAsync ──

    [Fact]
    public async Task Rename_PatchesName()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "id": "item-1",
            "name": "new-name.txt",
            "size": 100,
            "lastModifiedDateTime": "2025-06-15T10:30:00Z"
        }
        """);
        DriveService svc = CreateService(handler);

        DriveItemSummary result = await svc.RenameAsync("item-1", "new-name.txt", CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[1].Body.Should().Contain("new-name.txt");
        result.Name.Should().Be("new-name.txt");
    }

    // ── DeleteAsync ──

    [Fact]
    public async Task Delete_CallsDeleteEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.EnqueueEmpty();
        DriveService svc = CreateService(handler);

        await svc.DeleteAsync("item-to-delete", CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/items/item-to-delete");
    }

    // ── Helpers ──

    private static DriveService CreateService(MockGraphHandler handler)
    {
        return new DriveService(MockGraphHandler.CreateClient(handler));
    }
}
