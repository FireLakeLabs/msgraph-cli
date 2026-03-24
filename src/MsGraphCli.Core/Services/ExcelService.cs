using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using MsGraphCli.Core.Models;

namespace MsGraphCli.Core.Services;

public interface IExcelService
{
    Task<IReadOnlyList<WorksheetInfo>> ListWorksheetsAsync(string itemId, CancellationToken cancellationToken);
    Task<RangeData> GetRangeAsync(string itemId, string sheetName, string rangeAddress, CancellationToken cancellationToken);
    Task<RangeData> UpdateRangeAsync(string itemId, string sheetName, string rangeAddress, JsonElement values, CancellationToken cancellationToken);
    Task<TableRowsAdded> AppendTableRowsAsync(string itemId, string sheetName, string tableName, JsonElement values, CancellationToken cancellationToken);
}

public sealed class ExcelService : IExcelService
{
    private readonly GraphServiceClient _client;
    private string? _driveId;

    public ExcelService(GraphServiceClient client)
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

    public async Task<IReadOnlyList<WorksheetInfo>> ListWorksheetsAsync(string itemId, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        WorkbookWorksheetCollectionResponse? response = await _client.Drives[driveId].Items[itemId]
            .Workbook.Worksheets
            .GetAsync(cancellationToken: cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select((ws, index) => new WorksheetInfo(
            Id: ws.Id ?? "",
            Name: ws.Name ?? "",
            Visibility: ws.Visibility ?? "Visible",
            Position: ws.Position ?? index
        )).ToList();
    }

    public async Task<RangeData> GetRangeAsync(string itemId, string sheetName, string rangeAddress, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        WorkbookRange? range = await _client.Drives[driveId].Items[itemId]
            .Workbook.Worksheets[sheetName]
            .RangeWithAddress(rangeAddress)
            .GetAsync(cancellationToken: cancellationToken);

        if (range is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Range '{rangeAddress}' not found on sheet '{sheetName}'.");
        }

        return MapRange(range);
    }

    public async Task<RangeData> UpdateRangeAsync(string itemId, string sheetName, string rangeAddress, JsonElement values, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        // Build the URL from the GET request builder, then send as PATCH
        var rangeBuilder = _client.Drives[driveId].Items[itemId]
            .Workbook.Worksheets[sheetName]
            .RangeWithAddress(rangeAddress);

        RequestInformation requestInfo = rangeBuilder.ToGetRequestInformation();
        requestInfo.HttpMethod = Method.PATCH;

        var body = new WorkbookRange
        {
            Values = JsonElementToUntypedNode(values),
        };

        requestInfo.SetContentFromParsable(_client.RequestAdapter, "application/json", body);

        WorkbookRange? result = await _client.RequestAdapter.SendAsync(
            requestInfo,
            WorkbookRange.CreateFromDiscriminatorValue,
            cancellationToken: cancellationToken);

        if (result is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to update range '{rangeAddress}' on sheet '{sheetName}'.",
                "UpdateFailed", exitCode: 1);
        }

        return MapRange(result);
    }

    public async Task<TableRowsAdded> AppendTableRowsAsync(string itemId, string sheetName, string tableName, JsonElement values, CancellationToken cancellationToken)
    {
        string driveId = await GetDriveIdAsync(cancellationToken);

        // Count rows being added
        int rowCount = values.ValueKind == JsonValueKind.Array ? values.GetArrayLength() : 0;

        var body = new Microsoft.Graph.Drives.Item.Items.Item.Workbook.Worksheets.Item.Tables.Item.Rows.Add.AddPostRequestBody
        {
            Values = JsonElementToUntypedNode(values),
        };

        await _client.Drives[driveId].Items[itemId]
            .Workbook.Worksheets[sheetName]
            .Tables[tableName]
            .Rows.Add
            .PostAsync(body, cancellationToken: cancellationToken);

        return new TableRowsAdded(tableName, rowCount);
    }

    // ── Mapping helpers ──

    private static RangeData MapRange(WorkbookRange range)
    {
        return new RangeData(
            Address: range.Address ?? "",
            RowCount: range.RowCount ?? 0,
            ColumnCount: range.ColumnCount ?? 0,
            Values: UntypedNodeToJsonElement(range.Values),
            Formulas: range.Formulas is not null ? UntypedNodeToJsonElement(range.Formulas) : null,
            NumberFormats: range.NumberFormat is not null ? UntypedNodeToJsonElement(range.NumberFormat) : null
        );
    }

    // ── UntypedNode ↔ JsonElement conversion ──

    internal static UntypedNode JsonElementToUntypedNode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => new UntypedArray(
                element.EnumerateArray().Select(JsonElementToUntypedNode).ToList()),
            JsonValueKind.Object => new UntypedObject(
                element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToUntypedNode(p.Value))),
            JsonValueKind.String => new UntypedString(element.GetString() ?? ""),
            JsonValueKind.Number when element.TryGetInt32(out int i) => new UntypedInteger(i),
            JsonValueKind.Number when element.TryGetInt64(out long l) => new UntypedLong(l),
            JsonValueKind.Number when element.TryGetDecimal(out decimal d) => new UntypedDecimal(d),
            JsonValueKind.Number => new UntypedDouble(element.GetDouble()),
            JsonValueKind.True => new UntypedBoolean(true),
            JsonValueKind.False => new UntypedBoolean(false),
            JsonValueKind.Null or JsonValueKind.Undefined => new UntypedNull(),
            _ => new UntypedNull(),
        };
    }

    internal static JsonElement UntypedNodeToJsonElement(UntypedNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteUntypedNode(writer, node);
        }
        stream.Position = 0;
        using JsonDocument doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }

    private static void WriteUntypedNode(Utf8JsonWriter writer, UntypedNode? node)
    {
        switch (node)
        {
            case UntypedArray arr:
                writer.WriteStartArray();
                foreach (UntypedNode item in arr.GetValue())
                {
                    WriteUntypedNode(writer, item);
                }
                writer.WriteEndArray();
                break;

            case UntypedObject obj:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, UntypedNode> prop in obj.GetValue())
                {
                    writer.WritePropertyName(prop.Key);
                    WriteUntypedNode(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;

            case UntypedString str:
                writer.WriteStringValue(str.GetValue());
                break;

            case UntypedDouble dbl:
                writer.WriteNumberValue(dbl.GetValue());
                break;

            case UntypedInteger integer:
                writer.WriteNumberValue(integer.GetValue());
                break;

            case UntypedLong lng:
                writer.WriteNumberValue(lng.GetValue());
                break;

            case UntypedFloat flt:
                writer.WriteNumberValue(flt.GetValue());
                break;

            case UntypedDecimal dec:
                writer.WriteNumberValue(dec.GetValue());
                break;

            case UntypedBoolean bln:
                writer.WriteBooleanValue(bln.GetValue());
                break;

            case UntypedNull:
            case null:
                writer.WriteNullValue();
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }
}
