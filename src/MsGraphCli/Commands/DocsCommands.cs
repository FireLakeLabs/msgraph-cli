using System.CommandLine;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class DocsCommands
{
    public static Command Build(GlobalOptions global)
    {
        var docsCommand = new Command("docs", "Document operations (Word, PowerPoint)");

        docsCommand.Subcommands.Add(BuildExport(global));
        docsCommand.Subcommands.Add(BuildCat(global));

        return docsCommand;
    }

    private static (IDocumentService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["docs"], readOnly: true);
        HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
        var factory = new GraphClientFactory(authProvider, scopes, httpClient);
        bool useBeta = parseResult.GetValue(global.Beta);
        var client = factory.CreateClient(useBeta: useBeta);
        var service = new DocumentService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── msgraph docs export ──

    private static Command BuildExport(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the document" };
        var formatOption = new Option<string>("--format") { Description = "Export format (default: pdf)" };
        var outOption = new Option<string>("--out") { Description = "Output file path", Required = true };

        var command = new Command("export", "Export a document to another format");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(formatOption);
        command.Options.Add(outOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string format = parseResult.GetValue(formatOption) ?? "pdf";
            string outputPath = parseResult.GetValue(outOption)!;

            DocumentExportResult result = await service.ExportAsync(itemId, format, outputPath, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "exported", export = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Exported: {result.OutputPath} ({result.BytesWritten:N0} bytes)");
            }
        });

        return command;
    }

    // ── msgraph docs cat ──

    private static Command BuildCat(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the document" };
        var outDirOption = new Option<string?>("--out-dir") { Description = "Directory to save extracted images" };

        var command = new Command("cat", "Extract text content as markdown");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(outDirOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string? outDir = parseResult.GetValue(outDirOption);

            DocumentContent content = await service.ExtractContentAsync(itemId, cancellationToken);

            // Save images if --out-dir provided
            List<object>? imageInfo = null;
            if (content.Images.Count > 0)
            {
                if (outDir is not null)
                {
                    if (!Directory.Exists(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                    }

                    imageInfo = [];
                    foreach (ExtractedImage image in content.Images)
                    {
                        string path = Path.Combine(outDir, image.FileName);
                        await File.WriteAllBytesAsync(path, image.Data, cancellationToken);
                        Console.Error.WriteLine($"Saved: {path}");
                        imageInfo.Add(new { image.FileName, image.ContentType, savedTo = path });
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Note: {content.Images.Count} images found. Use --out-dir to extract them.");
                    imageInfo = content.Images
                        .Select(i => (object)new { i.FileName, i.ContentType, savedTo = (string?)null })
                        .ToList();
                }
            }

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { markdown = content.Markdown, images = imageInfo ?? [] }, Console.Out);
            }
            else
            {
                Console.Out.Write(content.Markdown);
                if (!content.Markdown.EndsWith('\n'))
                {
                    Console.Out.WriteLine();
                }
            }
        });

        return command;
    }
}
