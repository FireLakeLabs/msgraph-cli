using System.Text.Json;
using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Models;
using FireLakeLabs.MsGraphCli.Core.Services;
using FireLakeLabs.MsGraphCli.Tests.Unit.Helpers;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class ExcelServiceTests
{
    // ── ListWorksheetsAsync ──

    [Fact]
    public async Task ListWorksheets_ReturnsWorksheetInfoList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "value": [
                {"id": "ws-1", "name": "Sheet1", "visibility": "Visible", "position": 0},
                {"id": "ws-2", "name": "Data", "visibility": "Visible", "position": 1}
            ]
        }
        """);
        ExcelService svc = CreateService(handler);

        IReadOnlyList<WorksheetInfo> result = await svc.ListWorksheetsAsync("file-123", CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("ws-1");
        result[0].Name.Should().Be("Sheet1");
        result[0].Visibility.Should().Be("Visible");
        result[0].Position.Should().Be(0);
        result[1].Name.Should().Be("Data");
        result[1].Position.Should().Be(1);
    }

    [Fact]
    public async Task ListWorksheets_EmptyResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        ExcelService svc = CreateService(handler);

        IReadOnlyList<WorksheetInfo> result = await svc.ListWorksheetsAsync("file-123", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListWorksheets_CallsCorrectEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"value": []}""");
        ExcelService svc = CreateService(handler);

        await svc.ListWorksheetsAsync("file-123", CancellationToken.None);

        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/workbook/worksheets");
        handler.Requests[1].Uri.AbsolutePath.Should().Contain("/items/file-123/");
    }

    // ── GetRangeAsync ──

    [Fact]
    public async Task GetRange_CallsCorrectEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:B2",
            "rowCount": 2,
            "columnCount": 2,
            "values": [["Name", "Score"], ["Alice", 95]]
        }
        """);
        ExcelService svc = CreateService(handler);

        await svc.GetRangeAsync("file-123", "Sheet1", "A1:B2", CancellationToken.None);

        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("/workbook/worksheets/Sheet1/");
        path.Should().Contain("A1:B2");
    }

    [Fact]
    public async Task GetRange_MapsValuesCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:B2",
            "rowCount": 2,
            "columnCount": 2,
            "values": [["Name", "Score"], ["Alice", 95]],
            "formulas": [["Name", "Score"], ["Alice", "=SUM(B1)"]],
            "numberFormat": [["General", "General"], ["General", "0"]]
        }
        """);
        ExcelService svc = CreateService(handler);

        RangeData result = await svc.GetRangeAsync("file-123", "Sheet1", "A1:B2", CancellationToken.None);

        result.Address.Should().Be("Sheet1!A1:B2");
        result.RowCount.Should().Be(2);
        result.ColumnCount.Should().Be(2);
        result.Values.ValueKind.Should().Be(JsonValueKind.Array);

        // Verify first row
        JsonElement firstRow = result.Values[0];
        firstRow[0].GetString().Should().Be("Name");
        firstRow[1].GetString().Should().Be("Score");

        // Verify second row
        JsonElement secondRow = result.Values[1];
        secondRow[0].GetString().Should().Be("Alice");
        secondRow[1].GetInt32().Should().Be(95);
    }

    [Fact]
    public async Task GetRange_IncludesRowAndColumnCount()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:D10",
            "rowCount": 10,
            "columnCount": 4,
            "values": [[1,2,3,4]]
        }
        """);
        ExcelService svc = CreateService(handler);

        RangeData result = await svc.GetRangeAsync("file-123", "Sheet1", "A1:D10", CancellationToken.None);

        result.RowCount.Should().Be(10);
        result.ColumnCount.Should().Be(4);
    }

    // ── UpdateRangeAsync ──

    [Fact]
    public async Task UpdateRange_SendsPatchRequest()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:B1",
            "rowCount": 1,
            "columnCount": 2,
            "values": [["X", "Y"]]
        }
        """);
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["X", "Y"]]""");
        JsonElement values = doc.RootElement.Clone();

        await svc.UpdateRangeAsync("file-123", "Sheet1", "A1:B1", values, CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Patch);
    }

    [Fact]
    public async Task UpdateRange_CallsCorrectEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:B1",
            "rowCount": 1,
            "columnCount": 2,
            "values": [["X", "Y"]]
        }
        """);
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["X", "Y"]]""");
        JsonElement values = doc.RootElement.Clone();

        await svc.UpdateRangeAsync("file-123", "Sheet1", "A1:B1", values, CancellationToken.None);

        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("/workbook/worksheets/Sheet1/");
        path.Should().Contain("A1:B1");
    }

    [Fact]
    public async Task UpdateRange_BodyContainsValues()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""
        {
            "address": "Sheet1!A1:B1",
            "rowCount": 1,
            "columnCount": 2,
            "values": [["Hello", 42]]
        }
        """);
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["Hello", 42]]""");
        JsonElement values = doc.RootElement.Clone();

        await svc.UpdateRangeAsync("file-123", "Sheet1", "A1:B1", values, CancellationToken.None);

        handler.Requests[1].Body.Should().Contain("Hello");
        handler.Requests[1].Body.Should().Contain("42");
    }

    // ── AppendTableRowsAsync ──

    [Fact]
    public async Task AppendTableRows_PostsToCorrectEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"index": 5}""");
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["Bob", 88]]""");
        JsonElement values = doc.RootElement.Clone();

        await svc.AppendTableRowsAsync("file-123", "Sheet1", "Table1", values, CancellationToken.None);

        string path = Uri.UnescapeDataString(handler.Requests[1].Uri.AbsolutePath);
        path.Should().Contain("/tables/Table1/rows/add");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task AppendTableRows_BodyContainsValues()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"index": 5}""");
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["Bob", 88]]""");
        JsonElement values = doc.RootElement.Clone();

        await svc.AppendTableRowsAsync("file-123", "Sheet1", "Table1", values, CancellationToken.None);

        handler.Requests[1].Body.Should().Contain("Bob");
        handler.Requests[1].Body.Should().Contain("88");
    }

    [Fact]
    public async Task AppendTableRows_ReturnsCorrectCount()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"id": "test-drive-id"}""");
        handler.Enqueue("""{"index": 5}""");
        ExcelService svc = CreateService(handler);

        using JsonDocument doc = JsonDocument.Parse("""[["Bob", 88], ["Carol", 92]]""");
        JsonElement values = doc.RootElement.Clone();

        TableRowsAdded result = await svc.AppendTableRowsAsync("file-123", "Sheet1", "Table1", values, CancellationToken.None);

        result.TableName.Should().Be("Table1");
        result.RowsAdded.Should().Be(2);
    }

    // ── UntypedNode conversion tests ──

    [Fact]
    public void JsonElementToUntypedNode_RoundTrips()
    {
        using JsonDocument doc = JsonDocument.Parse("""[["text", 42, true, null, 3.14]]""");
        JsonElement original = doc.RootElement.Clone();

        var node = ExcelService.JsonElementToUntypedNode(original);
        JsonElement result = ExcelService.UntypedNodeToJsonElement(node);

        result.ValueKind.Should().Be(JsonValueKind.Array);
        JsonElement row = result[0];
        row[0].GetString().Should().Be("text");
        row[1].GetDouble().Should().Be(42);
        row[2].GetBoolean().Should().BeTrue();
        row[3].ValueKind.Should().Be(JsonValueKind.Null);
        row[4].GetDouble().Should().Be(3.14);
    }

    // ── Helper ──

    private static ExcelService CreateService(MockGraphHandler handler)
    {
        return new ExcelService(MockGraphHandler.CreateClient(handler));
    }
}
