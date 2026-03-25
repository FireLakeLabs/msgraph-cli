using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Core.Models;
using FireLakeLabs.MsGraphCli.Core.Services;
using FireLakeLabs.MsGraphCli.Tests.Unit.Helpers;
using Xunit;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class DocumentServiceTests
{
    private static readonly string[] HeaderCells = ["Name", "Score"];
    private static readonly string[] DataCells = ["Alice", "95"];

    // ── ExportAsync ──

    [Fact]
    public async Task Export_DownloadsContent_WritesFile()
    {
        byte[] pdfContent = Encoding.UTF8.GetBytes("%PDF-fake-content");
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.EnqueueBytes(pdfContent, "application/pdf");
        DocumentService svc = CreateService(handler);

        string outputPath = Path.Combine(Path.GetTempPath(), $"test-export-{Guid.NewGuid()}.pdf");
        try
        {
            DocumentExportResult result = await svc.ExportAsync("doc-123", "pdf", outputPath, CancellationToken.None);

            File.Exists(outputPath).Should().BeTrue();
            byte[] written = await File.ReadAllBytesAsync(outputPath);
            written.Should().BeEquivalentTo(pdfContent);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Export_ReturnsCorrectResult()
    {
        byte[] pdfContent = new byte[1024];
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.EnqueueBytes(pdfContent, "application/pdf");
        DocumentService svc = CreateService(handler);

        string outputPath = Path.Combine(Path.GetTempPath(), $"test-export-{Guid.NewGuid()}.pdf");
        try
        {
            DocumentExportResult result = await svc.ExportAsync("doc-123", "pdf", outputPath, CancellationToken.None);

            result.ItemId.Should().Be("doc-123");
            result.Format.Should().Be("pdf");
            result.OutputPath.Should().Be(outputPath);
            result.BytesWritten.Should().Be(1024);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Export_CallsContentEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.EnqueueBytes([0x25, 0x50, 0x44, 0x46], "application/pdf");
        DocumentService svc = CreateService(handler);

        string outputPath = Path.Combine(Path.GetTempPath(), $"test-export-{Guid.NewGuid()}.pdf");
        try
        {
            await svc.ExportAsync("doc-123", "pdf", outputPath, CancellationToken.None);

            handler.Requests[1].Uri.AbsolutePath.Should().Contain("/content");
            handler.Requests[1].Uri.Query.Should().Contain("format=pdf");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // ── ExtractContentAsync — delegates to ExtractDocx/ExtractPptx ──
    // We test the static extraction methods directly since they don't need Graph API mocking

    // ── ExtractDocx ──

    [Fact]
    public void ExtractDocx_BasicParagraphs_ReturnsMarkdown()
    {
        byte[] docxBytes = CreateMinimalDocx("Hello world", "Second paragraph");
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().Contain("Hello world");
        result.Markdown.Should().Contain("Second paragraph");
    }

    [Fact]
    public void ExtractDocx_HeadingStyle_ReturnsMarkdownHeading()
    {
        byte[] docxBytes = CreateDocxWithHeading("My Title", "Heading1");
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().Contain("# My Title");
    }

    [Fact]
    public void ExtractDocx_BoldText_ReturnsMarkdownBold()
    {
        byte[] docxBytes = CreateDocxWithFormatting("bold text", bold: true, italic: false);
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().Contain("**bold text**");
    }

    [Fact]
    public void ExtractDocx_ItalicText_ReturnsMarkdownItalic()
    {
        byte[] docxBytes = CreateDocxWithFormatting("italic text", bold: false, italic: true);
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().Contain("*italic text*");
    }

    [Fact]
    public void ExtractDocx_Table_ReturnsMarkdownTable()
    {
        byte[] docxBytes = CreateDocxWithTable(
            HeaderCells,
            DataCells);
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().Contain("| Name | Score |");
        result.Markdown.Should().Contain("| --- | --- |");
        result.Markdown.Should().Contain("| Alice | 95 |");
    }

    [Fact]
    public void ExtractDocx_EmptyDocument_ReturnsEmptyContent()
    {
        byte[] docxBytes = CreateMinimalDocx();
        using var stream = new MemoryStream(docxBytes);

        DocumentContent result = DocumentService.ExtractDocx(stream);

        result.Markdown.Should().BeEmpty();
        result.Images.Should().BeEmpty();
    }

    // ── ExtractPptx ──

    [Fact]
    public void ExtractPptx_BasicSlide_ReturnsSlideText()
    {
        byte[] pptxBytes = CreateMinimalPptx("Slide Title", "Slide body text");
        using var stream = new MemoryStream(pptxBytes);

        DocumentContent result = DocumentService.ExtractPptx(stream);

        result.Markdown.Should().Contain("## Slide 1");
        result.Markdown.Should().Contain("Slide Title");
        result.Markdown.Should().Contain("Slide body text");
    }

    // ── ExtractContentAsync format detection ──

    [Fact]
    public async Task ExtractContent_UnsupportedFormat_ThrowsException()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"id": "file-1", "name": "data.xlsx", "file": {"mimeType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"}}""");
        handler.EnqueueBytes([0x50, 0x4B], "application/octet-stream");
        DocumentService svc = CreateService(handler);

        Func<Task> act = () => svc.ExtractContentAsync("file-1", CancellationToken.None);

        await act.Should().ThrowAsync<MsGraphCliException>()
            .Where(e => e.ErrorCode == "UnsupportedFormat");
    }

    // ── Test data builders ──

    private static byte[] CreateMinimalDocx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            foreach (string text in paragraphs)
            {
                body.AppendChild(new Paragraph(
                    new Run(new Text(text))));
            }

            mainPart.Document.AppendChild(body);
        }
        return stream.ToArray();
    }

    private static byte[] CreateDocxWithHeading(string text, string styleId)
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            var para = new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = styleId }),
                new Run(new Text(text)));
            body.AppendChild(para);

            mainPart.Document.AppendChild(body);
        }
        return stream.ToArray();
    }

    private static byte[] CreateDocxWithFormatting(string text, bool bold, bool italic)
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            var runProps = new RunProperties();
            if (bold) runProps.AppendChild(new Bold());
            if (italic) runProps.AppendChild(new Italic());

            var run = new Run(runProps, new Text(text));
            body.AppendChild(new Paragraph(run));

            mainPart.Document.AppendChild(body);
        }
        return stream.ToArray();
    }

    private static byte[] CreateDocxWithTable(string[] headerRow, string[] dataRow)
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            var table = new Table();

            // Header row
            var hRow = new TableRow();
            foreach (string cell in headerRow)
            {
                hRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell)))));
            }
            table.AppendChild(hRow);

            // Data row
            var dRow = new TableRow();
            foreach (string cell in dataRow)
            {
                dRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell)))));
            }
            table.AppendChild(dRow);

            body.AppendChild(table);
            mainPart.Document.AppendChild(body);
        }
        return stream.ToArray();
    }

    private static byte[] CreateMinimalPptx(string title, string bodyText)
    {
        using var stream = new MemoryStream();
        using (PresentationDocument doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            PresentationPart presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new DocumentFormat.OpenXml.Presentation.Presentation(
                new SlideIdList(),
                new SlideSize { Cx = 9144000, Cy = 6858000 },
                new NotesSize { Cx = 6858000, Cy = 9144000 });

            SlidePart slidePart = presentationPart.AddNewPart<SlidePart>("rId2");
            slidePart.Slide = new Slide(
                new CommonSlideData(
                    new ShapeTree(
                        new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeProperties(
                            new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 1, Name = "" },
                            new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(new Drawing.TransformGroup()),
                        new Shape(
                            new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(
                                new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 2, Name = "Title" },
                                new DocumentFormat.OpenXml.Presentation.NonVisualShapeDrawingProperties(),
                                new ApplicationNonVisualDrawingProperties()),
                            new DocumentFormat.OpenXml.Presentation.ShapeProperties(),
                            new DocumentFormat.OpenXml.Presentation.TextBody(
                                new Drawing.BodyProperties(),
                                new Drawing.ListStyle(),
                                new Drawing.Paragraph(
                                    new Drawing.Run(
                                        new Drawing.RunProperties { Language = "en-US" },
                                        new Drawing.Text(title))))),
                        new Shape(
                            new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(
                                new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 3, Name = "Body" },
                                new DocumentFormat.OpenXml.Presentation.NonVisualShapeDrawingProperties(),
                                new ApplicationNonVisualDrawingProperties()),
                            new DocumentFormat.OpenXml.Presentation.ShapeProperties(),
                            new DocumentFormat.OpenXml.Presentation.TextBody(
                                new Drawing.BodyProperties(),
                                new Drawing.ListStyle(),
                                new Drawing.Paragraph(
                                    new Drawing.Run(
                                        new Drawing.RunProperties { Language = "en-US" },
                                        new Drawing.Text(bodyText))))))));

            SlideIdList slideIdList = presentationPart.Presentation.SlideIdList!;
            slideIdList.AppendChild(new SlideId { Id = 256, RelationshipId = "rId2" });
        }
        return stream.ToArray();
    }

    private static DocumentService CreateService(MockGraphHandler handler)
    {
        return new DocumentService(MockGraphHandler.CreateClient(handler));
    }
}
