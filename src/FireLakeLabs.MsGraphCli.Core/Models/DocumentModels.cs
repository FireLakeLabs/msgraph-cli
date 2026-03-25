namespace FireLakeLabs.MsGraphCli.Core.Models;

public record DocumentExportResult(string ItemId, string Format, string OutputPath, long BytesWritten);

public record DocumentContent(string Markdown, IReadOnlyList<ExtractedImage> Images);

public record ExtractedImage(string FileName, string ContentType, byte[] Data);
