using System.CommandLine;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Middleware;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class DriveCommands
{
    public static Command Build(GlobalOptions global)
    {
        var driveCommand = new Command("drive", "OneDrive file operations");

        driveCommand.Subcommands.Add(BuildLs(global));
        driveCommand.Subcommands.Add(BuildSearch(global));
        driveCommand.Subcommands.Add(BuildGet(global));
        driveCommand.Subcommands.Add(BuildDownload(global));
        driveCommand.Subcommands.Add(BuildUpload(global));
        driveCommand.Subcommands.Add(BuildMkdir(global));
        driveCommand.Subcommands.Add(BuildMove(global));
        driveCommand.Subcommands.Add(BuildRename(global));
        driveCommand.Subcommands.Add(BuildDelete(global));

        return driveCommand;
    }

    private static (IDriveService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global, bool readOnly = true)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["drive"], readOnly: readOnly);
        HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
        var factory = new GraphClientFactory(authProvider, scopes, httpClient);
        var client = factory.CreateClient();
        var service = new DriveService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── msgraph drive ls ──

    private static Command BuildLs(GlobalOptions global)
    {
        var pathOption = new Option<string?>("--path") { Description = "Remote folder path" };
        var folderOption = new Option<string?>("--folder") { Description = "Folder ID" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum items" };

        var command = new Command("ls", "List folder contents");
        command.Options.Add(pathOption);
        command.Options.Add(folderOption);
        command.Options.Add(maxOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string? path = parseResult.GetValue(pathOption);
            string? folderId = parseResult.GetValue(folderOption);
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<DriveItemSummary> items = await service.ListChildrenAsync(folderId, path, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { items }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteDriveItemTable(items);
            }
            else
            {
                foreach (DriveItemSummary item in items)
                {
                    Console.WriteLine($"{item.Name}\t{(item.IsFolder ? "folder" : "file")}\t{item.Size}\t{item.Id}");
                }
            }
        });

        return command;
    }

    // ── msgraph drive search ──

    private static Command BuildSearch(GlobalOptions global)
    {
        var queryArgument = new Argument<string>("query") { Description = "Search query" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum results" };

        var command = new Command("search", "Search for files");
        command.Arguments.Add(queryArgument);
        command.Options.Add(maxOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string query = parseResult.GetValue(queryArgument)!;
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<DriveItemSummary> items = await service.SearchAsync(query, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { items, query }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteDriveItemTable(items);
            }
            else
            {
                foreach (DriveItemSummary item in items)
                {
                    Console.WriteLine($"{item.Name}\t{(item.IsFolder ? "folder" : "file")}\t{item.Size}\t{item.Id}");
                }
            }
        });

        return command;
    }

    // ── msgraph drive get ──

    private static Command BuildGet(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Item ID" };

        var command = new Command("get", "Get item details");
        command.Arguments.Add(itemIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string itemId = parseResult.GetValue(itemIdArgument)!;
            DriveItemDetail item = await service.GetItemAsync(itemId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { item }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteDriveItemDetailTable(item);
            }
            else
            {
                Console.WriteLine($"Name:     {item.Name}");
                Console.WriteLine($"Type:     {(item.IsFolder ? "folder" : item.MimeType ?? "file")}");
                Console.WriteLine($"Size:     {item.Size}");
                Console.WriteLine($"Created:  {item.Created:u}");
                Console.WriteLine($"Modified: {item.LastModified:u}");
                Console.WriteLine($"ID:       {item.Id}");

                if (item.ParentPath is not null)
                {
                    Console.WriteLine($"Parent:   {item.ParentPath}");
                }

                if (item.WebUrl is not null)
                {
                    Console.WriteLine($"URL:      {item.WebUrl}");
                }

                if (item.DownloadUrl is not null)
                {
                    Console.WriteLine($"Download: {item.DownloadUrl}");
                }
            }
        });

        return command;
    }

    // ── msgraph drive download ──

    private static Command BuildDownload(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string?>("itemId") { Description = "Item ID", Arity = ArgumentArity.ZeroOrOne };
        var pathOption = new Option<string?>("--path") { Description = "Remote file path" };
        var outOption = new Option<string>("--out") { Description = "Local output path", Required = true };

        var command = new Command("download", "Download a file");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(pathOption);
        command.Options.Add(outOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string? itemId = parseResult.GetValue(itemIdArgument);
            string? remotePath = parseResult.GetValue(pathOption);
            string outputPath = parseResult.GetValue(outOption)!;

            DriveItemSummary item = await service.DownloadAsync(itemId, remotePath, outputPath, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "downloaded", item, localPath = outputPath }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Downloaded: {item.Name} -> {outputPath}");
            }
        });

        return command;
    }

    // ── msgraph drive upload ──

    private static Command BuildUpload(GlobalOptions global)
    {
        var localPathArgument = new Argument<string>("localPath") { Description = "Local file path" };
        var pathOption = new Option<string>("--path") { Description = "Remote destination path", Required = true };

        var command = new Command("upload", "Upload a file");
        command.Arguments.Add(localPathArgument);
        command.Options.Add(pathOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("drive upload", parseResult.GetValue(global.ReadOnly));

            string localPath = parseResult.GetValue(localPathArgument)!;
            string remotePath = parseResult.GetValue(pathOption)!;

            if (parseResult.GetValue(global.DryRun))
            {
                var (_, dryFormatter) = CreateServiceContext(parseResult, global, readOnly: false);
                bool isDryJson = parseResult.GetValue(global.Json);
                if (isDryJson)
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "upload", details = new { localPath, remotePath } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would upload: {localPath} -> {remotePath}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            DriveItemSummary result = await service.UploadAsync(localPath, remotePath, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "uploaded", item = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Uploaded: {localPath} -> {result.Name} ({result.Id})");
            }
        });

        return command;
    }

    // ── msgraph drive mkdir ──

    private static Command BuildMkdir(GlobalOptions global)
    {
        var nameArgument = new Argument<string>("name") { Description = "Folder name" };
        var pathOption = new Option<string?>("--path") { Description = "Parent folder path" };

        var command = new Command("mkdir", "Create a folder");
        command.Arguments.Add(nameArgument);
        command.Options.Add(pathOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("drive mkdir", parseResult.GetValue(global.ReadOnly));

            string name = parseResult.GetValue(nameArgument)!;
            string? parentPath = parseResult.GetValue(pathOption);

            if (parseResult.GetValue(global.DryRun))
            {
                var (_, dryFormatter) = CreateServiceContext(parseResult, global, readOnly: false);
                bool isDryJson = parseResult.GetValue(global.Json);
                if (isDryJson)
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "mkdir", details = new { name, parentPath } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would create folder: {name} in {parentPath ?? "root"}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            DriveItemSummary result = await service.CreateFolderAsync(name, parentPath, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "created", item = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Created folder: {result.Name} ({result.Id})");
            }
        });

        return command;
    }

    // ── msgraph drive move ──

    private static Command BuildMove(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Item ID to move" };
        var destinationOption = new Option<string>("--destination") { Description = "Destination folder path", Required = true };

        var command = new Command("move", "Move an item");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(destinationOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("drive move", parseResult.GetValue(global.ReadOnly));

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string destination = parseResult.GetValue(destinationOption)!;

            if (parseResult.GetValue(global.DryRun))
            {
                var (_, dryFormatter) = CreateServiceContext(parseResult, global, readOnly: false);
                bool isDryJson = parseResult.GetValue(global.Json);
                if (isDryJson)
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "move", details = new { itemId, destination } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would move: {itemId} -> {destination}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            DriveItemSummary result = await service.MoveAsync(itemId, destination, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "moved", item = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Moved: {result.Name} ({result.Id})");
            }
        });

        return command;
    }

    // ── msgraph drive rename ──

    private static Command BuildRename(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Item ID to rename" };
        var newNameArgument = new Argument<string>("newName") { Description = "New name" };

        var command = new Command("rename", "Rename an item");
        command.Arguments.Add(itemIdArgument);
        command.Arguments.Add(newNameArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("drive rename", parseResult.GetValue(global.ReadOnly));

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string newName = parseResult.GetValue(newNameArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                var (_, dryFormatter) = CreateServiceContext(parseResult, global, readOnly: false);
                bool isDryJson = parseResult.GetValue(global.Json);
                if (isDryJson)
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "rename", details = new { itemId, newName } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would rename: {itemId} -> {newName}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            DriveItemSummary result = await service.RenameAsync(itemId, newName, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "renamed", item = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Renamed: {result.Name} ({result.Id})");
            }
        });

        return command;
    }

    // ── msgraph drive delete ──

    private static Command BuildDelete(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Item ID to delete" };

        var command = new Command("delete", "Delete an item");
        command.Arguments.Add(itemIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("drive delete", parseResult.GetValue(global.ReadOnly));

            string itemId = parseResult.GetValue(itemIdArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                var (_, dryFormatter) = CreateServiceContext(parseResult, global, readOnly: false);
                bool isDryJson = parseResult.GetValue(global.Json);
                if (isDryJson)
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "delete", details = new { itemId } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would delete: {itemId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            await service.DeleteAsync(itemId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "deleted", itemId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("Item deleted.");
            }
        });

        return command;
    }
}
