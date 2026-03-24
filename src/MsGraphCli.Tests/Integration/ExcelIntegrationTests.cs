using System.Text.Json;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for Excel operations.
/// Requires authenticated 1Password session, MSGRAPH_LIVE=1,
/// and a test .xlsx file uploaded to OneDrive.
/// </summary>
[Trait("Category", "Integration")]
public class ExcelIntegrationTests : IntegrationTestBase
{
    private const string TestFolderPath = "/msgraph-cli-test";
    private const string TestFileName = "integration-test.xlsx";

    [Fact]
    public async Task ListWorksheets_OnTestFile()
    {
        if (!IsLiveTestEnabled) return;

        // First upload a minimal xlsx, then test Excel operations
        DriveService driveService = CreateDriveService();
        string? itemId = await UploadTestWorkbook(driveService);
        if (itemId is null) return;

        try
        {
            ExcelService excelService = CreateExcelService();
            IReadOnlyList<WorksheetInfo> sheets = await excelService.ListWorksheetsAsync(itemId, CancellationToken.None);
            Assert.NotEmpty(sheets);
        }
        finally
        {
            await driveService.DeleteAsync(itemId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReadRange_OnTestFile()
    {
        if (!IsLiveTestEnabled) return;

        DriveService driveService = CreateDriveService();
        string? itemId = await UploadTestWorkbook(driveService);
        if (itemId is null) return;

        try
        {
            ExcelService excelService = CreateExcelService();
            RangeData range = await excelService.GetRangeAsync(itemId, "Sheet1", "A1:B2", CancellationToken.None);
            Assert.NotNull(range);
            Assert.NotEqual(default, range.Values);
        }
        finally
        {
            await driveService.DeleteAsync(itemId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Creates a minimal .xlsx file and uploads it, returning the item ID.
    /// Uses DocumentFormat.OpenXml to build the workbook.
    /// </summary>
    private static async Task<string?> UploadTestWorkbook(DriveService driveService)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.xlsx");
        try
        {
            CreateMinimalXlsx(tempPath);
            string remotePath = $"{TestFolderPath}/{TestFileName}";
            DriveItemSummary uploaded = await driveService.UploadAsync(tempPath, remotePath, CancellationToken.None);
            // Small delay for Excel Online to process the file
            await Task.Delay(2000);
            return uploaded.Id;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void CreateMinimalXlsx(string path)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(
            path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);

        var workbookPart = doc.AddWorkbookPart();
        workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

        var worksheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
        var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();

        // Add a row with data
        var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            CellReference = "A1",
            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Name"),
        });
        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            CellReference = "B1",
            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Score"),
        });
        sheetData.Append(row);

        var row2 = new DocumentFormat.OpenXml.Spreadsheet.Row();
        row2.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            CellReference = "A2",
            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Test"),
        });
        row2.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            CellReference = "B2",
            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.Number,
            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("42"),
        });
        sheetData.Append(row2);

        worksheetPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(sheetData);

        var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
        sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Sheet1",
        });

        doc.Save();
    }
}
