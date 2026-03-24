using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for document operations (Word export, text extraction).
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class DocsIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ExportToPdf_OnUploadedDocx()
    {
        if (!IsLiveTestEnabled) return;

        DriveService driveService = CreateDriveService();

        // Upload a minimal .docx
        string tempDocx = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.docx");
        CreateMinimalDocx(tempDocx);

        string remotePath = $"/msgraph-cli-test/doc-integration-test-{Guid.NewGuid():N}.docx";
        DriveItemSummary uploaded = await driveService.UploadAsync(tempDocx, remotePath, CancellationToken.None);
        File.Delete(tempDocx);

        try
        {
            // Allow time for OneDrive to process
            await Task.Delay(2000);

            DocumentService docService = CreateDocumentService();
            string outputPdf = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.pdf");

            try
            {
                DocumentExportResult result = await docService.ExportAsync(uploaded.Id, "pdf", outputPdf, CancellationToken.None);
                Assert.True(File.Exists(outputPdf));
                Assert.True(new FileInfo(outputPdf).Length > 0);
            }
            finally
            {
                if (File.Exists(outputPdf)) File.Delete(outputPdf);
            }
        }
        finally
        {
            await driveService.DeleteAsync(uploaded.Id, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExtractContent_OnUploadedDocx()
    {
        if (!IsLiveTestEnabled) return;

        DriveService driveService = CreateDriveService();

        string tempDocx = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.docx");
        CreateMinimalDocx(tempDocx);

        string remotePath = $"/msgraph-cli-test/doc-cat-test-{Guid.NewGuid():N}.docx";
        DriveItemSummary uploaded = await driveService.UploadAsync(tempDocx, remotePath, CancellationToken.None);
        File.Delete(tempDocx);

        try
        {
            await Task.Delay(2000);

            DocumentService docService = CreateDocumentService();
            DocumentContent content = await docService.ExtractContentAsync(uploaded.Id, CancellationToken.None);
            Assert.NotNull(content);
            Assert.Contains("Integration test", content.Markdown);
        }
        finally
        {
            await driveService.DeleteAsync(uploaded.Id, CancellationToken.None);
        }
    }

    private static void CreateMinimalDocx(string path)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
        var body = mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());
        var para = body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph());
        var run = para.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run());
        run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text("Integration test document content"));
        doc.Save();
    }
}
