using System.Text.Json;

namespace FireLakeLabs.MsGraphCli.Core.Models;

public record WorksheetInfo(string Id, string Name, string Visibility, int Position);

public record RangeData(
    string Address,
    int RowCount,
    int ColumnCount,
    JsonElement Values,
    JsonElement? Formulas,
    JsonElement? NumberFormats);

public record TableRowsAdded(string TableName, int RowsAdded);
