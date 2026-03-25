using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using FireLakeLabs.MsGraphCli.Core.Models;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace FireLakeLabs.MsGraphCli.Core.Services;

public interface IDocumentService
{
    Task<DocumentExportResult> ExportAsync(string itemId, string format, string outputPath, CancellationToken cancellationToken);
    Task<DocumentContent> ExtractContentAsync(string itemId, CancellationToken cancellationToken);
}

public sealed class DocumentService : IDocumentService
{
    private readonly GraphServiceClient _client;
    private string? _driveId;

    public DocumentService(GraphServiceClient client)
    {
        _client = client;
    }

    private async Task<string> GetDriveIdAsync(CancellationToken cancellationToken)
    {
        if (_driveId is null)
        {
            Drive? drive = await _client.Me.Drive.GetAsync(cancellationToken: cancellationToken);
            _driveId = drive?.Id ?? throw new Exceptions.MsGraphCliException(
                "Could not retrieve user's drive.", "DriveNotFound", exitCode: 1);
        }
        return _driveId;
    }

    public async Task<DocumentExportResult> ExportAsync(string itemId, string format, string outputPath, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        // Use the content endpoint with format query parameter for conversion
        // The SDK's Content.GetAsync doesn't expose format, so we build the URL manually
        var requestInfo = _client.Drives[driveId].Items[itemId].Content.ToGetRequestInformation();
        requestInfo.UrlTemplate += "{?format}";
        requestInfo.QueryParameters.Add("format", format);

        Stream? stream = await _client.RequestAdapter.SendPrimitiveAsync<Stream>(
            requestInfo, cancellationToken: cancellationToken);

        if (stream is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Could not export item '{itemId}' to format '{format}'.");
        }

        string directory = Path.GetDirectoryName(outputPath) ?? ".";
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long bytesWritten;
        await using (stream)
        await using (FileStream fileStream = File.Create(outputPath))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
            bytesWritten = fileStream.Length;
        }

        return new DocumentExportResult(itemId, format, outputPath, bytesWritten);
    }

    public async Task<DocumentContent> ExtractContentAsync(string itemId, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        // Get item metadata to determine file type
        DriveItem? item = await _client.Drives[driveId].Items[itemId].GetAsync(
            config => config.QueryParameters.Select = ["id", "name", "file"],
            cancellationToken: cancellationToken);

        if (item is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Drive item '{itemId}' not found.");
        }

        string extension = Path.GetExtension(item.Name ?? "").ToLowerInvariant();

        // Download raw content
        Stream? content = await _client.Drives[driveId].Items[itemId].Content
            .GetAsync(cancellationToken: cancellationToken);

        if (content is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Could not download content for item '{itemId}'.");
        }

        // Copy to memory stream so we can seek
        using var memoryStream = new MemoryStream();
        await using (content)
        {
            await content.CopyToAsync(memoryStream, cancellationToken);
        }
        memoryStream.Position = 0;

        return extension switch
        {
            ".docx" => ExtractDocx(memoryStream),
            ".pptx" => ExtractPptx(memoryStream),
            _ => throw new Exceptions.MsGraphCliException(
                $"Unsupported file format '{extension}'. Supported formats: .docx, .pptx",
                "UnsupportedFormat", exitCode: 1),
        };
    }

    // ── Word (.docx) extraction ──

    internal static DocumentContent ExtractDocx(Stream stream)
    {
        using WordprocessingDocument doc = WordprocessingDocument.Open(stream, false);
        var images = new List<ExtractedImage>();
        var markdown = new StringBuilder();

        MainDocumentPart? mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is null)
        {
            return new DocumentContent("", []);
        }

        Body body = mainPart!.Document!.Body!;
        int imageCounter = 0;

        foreach (OpenXmlElement element in body.ChildElements)
        {
            if (element is Paragraph para)
            {
                string paraMarkdown = ConvertParagraphToMarkdown(para, mainPart, images, ref imageCounter);
                if (!string.IsNullOrEmpty(paraMarkdown))
                {
                    markdown.AppendLine(paraMarkdown);
                    markdown.AppendLine();
                }
            }
            else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                string tableMarkdown = ConvertTableToMarkdown(table, mainPart, images, ref imageCounter);
                markdown.AppendLine(tableMarkdown);
                markdown.AppendLine();
            }
        }

        return new DocumentContent(markdown.ToString().TrimEnd(), images);
    }

    private static string ConvertParagraphToMarkdown(
        Paragraph para, MainDocumentPart mainPart, List<ExtractedImage> images, ref int imageCounter)
    {
        // Detect heading level from paragraph style
        string? styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        int headingLevel = GetHeadingLevel(styleId);

        // Check for list items
        bool isBullet = para.ParagraphProperties?.NumberingProperties is not null;

        var sb = new StringBuilder();

        foreach (OpenXmlElement child in para.ChildElements)
        {
            if (child is Run run)
            {
                sb.Append(ConvertRunToMarkdown(run));
            }
            else if (child is Hyperlink hyperlink)
            {
                string text = string.Concat(hyperlink.Descendants<Text>().Select(t => t.Text));
                string? relId = hyperlink.Id?.Value;
                if (relId is not null)
                {
                    HyperlinkRelationship? rel = mainPart.HyperlinkRelationships
                        .FirstOrDefault(r => r.Id == relId);
                    if (rel is not null)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"[{text}]({rel.Uri})");
                        continue;
                    }
                }
                sb.Append(text);
            }
        }

        // Extract images from drawings in this paragraph
        foreach (Drawing.Blip blip in para.Descendants<Drawing.Blip>())
        {
            string? embedId = blip.Embed?.Value;
            if (embedId is not null)
            {
                OpenXmlPart? imagePart = mainPart.GetPartById(embedId);
                if (imagePart is not null)
                {
                    imageCounter++;
                    string contentType = imagePart.ContentType;
                    string ext = GetImageExtension(contentType);
                    string fileName = string.Create(CultureInfo.InvariantCulture, $"image{imageCounter}{ext}");

                    using Stream imgStream = imagePart.GetStream();
                    using var ms = new MemoryStream();
                    imgStream.CopyTo(ms);
                    images.Add(new ExtractedImage(fileName, contentType, ms.ToArray()));

                    sb.Append(CultureInfo.InvariantCulture, $"![image]({fileName})");
                }
            }
        }

        string content = sb.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        if (headingLevel > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{new string('#', headingLevel)} {content}");
        }

        if (isBullet)
        {
            return string.Create(CultureInfo.InvariantCulture, $"- {content}");
        }

        return content;
    }

    private static string ConvertRunToMarkdown(Run run)
    {
        string text = string.Concat(run.Descendants<Text>().Select(t => t.Text));
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        RunProperties? props = run.RunProperties;
        bool isBold = props?.Bold is not null && (props.Bold.Val is null || props.Bold.Val.Value);
        bool isItalic = props?.Italic is not null && (props.Italic.Val is null || props.Italic.Val.Value);

        if (isBold && isItalic)
        {
            return $"***{text}***";
        }
        if (isBold)
        {
            return $"**{text}**";
        }
        if (isItalic)
        {
            return $"*{text}*";
        }

        return text;
    }

    private static string ConvertTableToMarkdown(
        DocumentFormat.OpenXml.Wordprocessing.Table table, MainDocumentPart mainPart,
        List<ExtractedImage> images, ref int imageCounter)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();

        for (int i = 0; i < rows.Count; i++)
        {
            var cells = new List<string>();
            foreach (TableCell cell in rows[i].Elements<TableCell>())
            {
                var cellSb = new StringBuilder();
                foreach (Paragraph p in cell.Elements<Paragraph>())
                {
                    string pText = ConvertParagraphToMarkdown(p, mainPart, images, ref imageCounter);
                    if (!string.IsNullOrEmpty(pText))
                    {
                        if (cellSb.Length > 0)
                        {
                            cellSb.Append(' ');
                        }
                        cellSb.Append(pText);
                    }
                }
                cells.Add(cellSb.ToString());
            }

            sb.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");

            // Add header separator after first row
            if (i == 0)
            {
                sb.Append("| ").Append(string.Join(" | ", cells.Select(_ => "---"))).AppendLine(" |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static int GetHeadingLevel(string? styleId)
    {
        if (styleId is null) return 0;
        return styleId switch
        {
            "Heading1" => 1,
            "Heading2" => 2,
            "Heading3" => 3,
            "Heading4" => 4,
            "Heading5" => 5,
            "Heading6" => 6,
            "Title" => 1,
            "Subtitle" => 2,
            _ => 0,
        };
    }

    // ── PowerPoint (.pptx) extraction ──

    internal static DocumentContent ExtractPptx(Stream stream)
    {
        using PresentationDocument doc = PresentationDocument.Open(stream, false);
        var images = new List<ExtractedImage>();
        var markdown = new StringBuilder();

        PresentationPart? presentationPart = doc.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
        {
            return new DocumentContent("", []);
        }

        int slideNumber = 0;
        int imageCounter = 0;

        foreach (SlideId slideId in presentationPart.Presentation.SlideIdList.Elements<SlideId>())
        {
            string? relId = slideId.RelationshipId?.Value;
            if (relId is null) continue;

            SlidePart slidePart = (SlidePart)presentationPart.GetPartById(relId);
            slideNumber++;

            markdown.Append(CultureInfo.InvariantCulture, $"## Slide {slideNumber}").AppendLine();
            markdown.AppendLine();

            if (slidePart.Slide?.CommonSlideData?.ShapeTree is null) continue;

            foreach (Shape shape in slidePart.Slide.CommonSlideData.ShapeTree.Elements<Shape>())
            {
                var textBody = shape.TextBody;
                if (textBody is null) continue;

                foreach (Drawing.Paragraph para in textBody.Elements<Drawing.Paragraph>())
                {
                    string text = string.Concat(
                        para.Descendants<Drawing.Text>().Select(t => t.Text));

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        markdown.AppendLine(text);
                        markdown.AppendLine();
                    }
                }
            }

            // Extract images from slide
            if (slidePart.Slide is null) continue;
            foreach (Drawing.Blip blip in slidePart.Slide.Descendants<Drawing.Blip>())
            {
                string? embedId = blip.Embed?.Value;
                if (embedId is null) continue;

                OpenXmlPart? imagePart = slidePart.GetPartById(embedId);
                if (imagePart is null) continue;

                imageCounter++;
                string contentType = imagePart.ContentType;
                string ext = GetImageExtension(contentType);
                string fileName = string.Create(CultureInfo.InvariantCulture, $"slide{slideNumber}_image{imageCounter}{ext}");

                using Stream imgStream = imagePart.GetStream();
                using var ms = new MemoryStream();
                imgStream.CopyTo(ms);
                images.Add(new ExtractedImage(fileName, contentType, ms.ToArray()));

                markdown.Append(CultureInfo.InvariantCulture, $"![image]({fileName})").AppendLine();
                markdown.AppendLine();
            }
        }

        return new DocumentContent(markdown.ToString().TrimEnd(), images);
    }

    // ── Shared helpers ──

    private static string GetImageExtension(string contentType)
    {
        return contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            "image/tiff" => ".tiff",
            _ => ".bin",
        };
    }
}
