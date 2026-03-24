using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for OneDrive operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class DriveIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ListRoot_ReturnsItems()
    {
        if (!IsLiveTestEnabled) return;

        DriveService service = CreateDriveService(readOnly: true);
        IReadOnlyList<DriveItemSummary> items = await service.ListChildrenAsync(null, null, 10, CancellationToken.None);

        Assert.NotNull(items);
        // Root may be empty on a fresh account, just verify the call succeeds
    }

    [Fact]
    public async Task UploadDownloadDelete_RoundTrip()
    {
        if (!IsLiveTestEnabled) return;

        DriveService service = CreateDriveService();
        string testContent = $"Integration test content {Guid.NewGuid()}";
        string remotePath = $"/msgraph-cli-test/integration-test-{Guid.NewGuid():N}.txt";
        string localPath = Path.GetTempFileName();

        try
        {
            // Write test content to a local temp file
            await File.WriteAllTextAsync(localPath, testContent);

            // Upload
            DriveItemSummary uploaded = await service.UploadAsync(localPath, remotePath, CancellationToken.None);
            Assert.NotNull(uploaded.Id);
            Assert.Contains("integration-test-", uploaded.Name);

            // Get metadata
            DriveItemDetail detail = await service.GetItemAsync(uploaded.Id, CancellationToken.None);
            Assert.Equal(uploaded.Id, detail.Id);
            Assert.False(detail.IsFolder);

            // Download
            string downloadPath = Path.GetTempFileName();
            try
            {
                DriveItemSummary downloaded = await service.DownloadAsync(uploaded.Id, null, downloadPath, CancellationToken.None);
                string downloadedContent = await File.ReadAllTextAsync(downloadPath);
                Assert.Equal(testContent, downloadedContent);
            }
            finally
            {
                File.Delete(downloadPath);
            }

            // Delete
            await service.DeleteAsync(uploaded.Id, CancellationToken.None);
        }
        finally
        {
            File.Delete(localPath);
        }
    }

    [Fact]
    public async Task Search_ReturnsResults()
    {
        if (!IsLiveTestEnabled) return;

        DriveService service = CreateDriveService(readOnly: true);

        // Search for any file — results depend on account contents
        IReadOnlyList<DriveItemSummary> results = await service.SearchAsync("test", 5, CancellationToken.None);
        Assert.NotNull(results);
    }
}
