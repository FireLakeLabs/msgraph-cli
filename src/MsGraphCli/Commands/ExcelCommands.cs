using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Middleware;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class ExcelCommands
{
    public static Command Build(GlobalOptions global)
    {
        var excelCommand = new Command("excel", "Excel workbook operations");

        excelCommand.Subcommands.Add(BuildSheets(global));
        excelCommand.Subcommands.Add(BuildGet(global));
        excelCommand.Subcommands.Add(BuildUpdate(global));
        excelCommand.Subcommands.Add(BuildAppend(global));

        return excelCommand;
    }

    private static (IExcelService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global, bool readOnly = true)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["excel"], readOnly: readOnly);
        HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
        var factory = new GraphClientFactory(authProvider, scopes, httpClient);
        bool useBeta = parseResult.GetValue(global.Beta);
        var client = factory.CreateClient(useBeta: useBeta);
        var service = new ExcelService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── msgraph excel sheets ──

    private static Command BuildSheets(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the Excel file" };

        var command = new Command("sheets", "List worksheets in a workbook");
        command.Arguments.Add(itemIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string itemId = parseResult.GetValue(itemIdArgument)!;

            IReadOnlyList<WorksheetInfo> worksheets = await service.ListWorksheetsAsync(itemId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { worksheets }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteWorksheetTable(worksheets);
            }
            else
            {
                foreach (WorksheetInfo ws in worksheets)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{ws.Name}\t{ws.Visibility}\t{ws.Position}\t{ws.Id}"));
                }
            }
        });

        return command;
    }

    // ── msgraph excel get ──

    private static Command BuildGet(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the Excel file" };
        var sheetOption = new Option<string>("--sheet") { Description = "Worksheet name", Required = true };
        var rangeOption = new Option<string>("--range") { Description = "Range address (e.g. A1:D20)", Required = true };

        var command = new Command("get", "Read a range from a worksheet");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(sheetOption);
        command.Options.Add(rangeOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string sheet = parseResult.GetValue(sheetOption)!;
            string range = parseResult.GetValue(rangeOption)!;

            RangeData rangeData = await service.GetRangeAsync(itemId, sheet, range, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { range = rangeData }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteRangeTable(rangeData);
            }
            else
            {
                // Plain: tab-separated values
                if (rangeData.Values.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement row in rangeData.Values.EnumerateArray())
                    {
                        if (row.ValueKind == JsonValueKind.Array)
                        {
                            Console.WriteLine(string.Join("\t",
                                row.EnumerateArray().Select(cell => cell.ToString())));
                        }
                    }
                }
            }
        });

        return command;
    }

    // ── msgraph excel update ──

    private static Command BuildUpdate(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the Excel file" };
        var sheetOption = new Option<string>("--sheet") { Description = "Worksheet name", Required = true };
        var rangeOption = new Option<string>("--range") { Description = "Range address (e.g. A1:B2)", Required = true };
        var valuesOption = new Option<string>("--values") { Description = "JSON 2D array of values (e.g. '[[\"A\",1],[\"B\",2]]')", Required = true };

        var command = new Command("update", "Update cell values in a range");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(sheetOption);
        command.Options.Add(rangeOption);
        command.Options.Add(valuesOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("excel update", parseResult.GetValue(global.ReadOnly));

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string sheet = parseResult.GetValue(sheetOption)!;
            string range = parseResult.GetValue(rangeOption)!;
            string valuesJson = parseResult.GetValue(valuesOption)!;

            JsonElement values = ParseValuesJson(valuesJson);

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "update", details = new { itemId, sheet, range, values } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would update: {sheet}!{range} in {itemId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            RangeData result = await service.UpdateRangeAsync(itemId, sheet, range, values, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "updated", range = result }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Updated: {result.Address} ({result.RowCount} rows × {result.ColumnCount} columns)");
            }
        });

        return command;
    }

    // ── msgraph excel append ──

    private static Command BuildAppend(GlobalOptions global)
    {
        var itemIdArgument = new Argument<string>("itemId") { Description = "Drive item ID of the Excel file" };
        var sheetOption = new Option<string>("--sheet") { Description = "Worksheet name", Required = true };
        var tableOption = new Option<string>("--table") { Description = "Table name", Required = true };
        var valuesOption = new Option<string>("--values") { Description = "JSON 2D array of row values", Required = true };

        var command = new Command("append", "Append rows to a table");
        command.Arguments.Add(itemIdArgument);
        command.Options.Add(sheetOption);
        command.Options.Add(tableOption);
        command.Options.Add(valuesOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("excel append", parseResult.GetValue(global.ReadOnly));

            string itemId = parseResult.GetValue(itemIdArgument)!;
            string sheet = parseResult.GetValue(sheetOption)!;
            string tableName = parseResult.GetValue(tableOption)!;
            string valuesJson = parseResult.GetValue(valuesOption)!;

            JsonElement values = ParseValuesJson(valuesJson);

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "append", details = new { itemId, sheet, table = tableName, values } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would append rows to table '{tableName}' in {itemId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            TableRowsAdded result = await service.AppendTableRowsAsync(itemId, sheet, tableName, values, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "appended", table = result.TableName, rowsAdded = result.RowsAdded }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Appended {result.RowsAdded} rows to table '{result.TableName}'.");
            }
        });

        return command;
    }

    // ── Helpers ──

    private static JsonElement ParseValuesJson(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new Core.Exceptions.MsGraphCliException(
                    "Values must be a JSON array of arrays (e.g. '[[\"a\",1],[\"b\",2]]').",
                    "InvalidArgument", exitCode: 1);
            }

            // Validate it's an array of arrays
            foreach (JsonElement row in root.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                {
                    throw new Core.Exceptions.MsGraphCliException(
                        "Each element in values must be an array (e.g. '[[\"a\",1],[\"b\",2]]').",
                        "InvalidArgument", exitCode: 1);
                }
            }

            return root.Clone();
        }
        catch (JsonException ex)
        {
            throw new Core.Exceptions.MsGraphCliException(
                $"Invalid JSON for --values: {ex.Message}",
                "InvalidArgument", exitCode: 1, inner: ex);
        }
    }
}
