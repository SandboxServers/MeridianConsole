using System.Text.Json;
using Dhadgar.Lab.Data;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <see cref="LabDb.BuildResult"/> wiring: origin lookup, the catalog-miss and whole-row fallbacks, the null rule, and kept names.
/// </summary>
public sealed class LabDbBuildResultTests
{
    [Fact]
    public void BuildResult_MasksByOrigin_KeepsNullsAndNames()
    {
        var raw = new LabDb.RawRows(
            [
                new LabDb.ColumnInfo("t", 10u, 2, "text"),
                new LabDb.ColumnInfo("id", 10u, 1, "integer"),
                new LabDb.ColumnInfo("x", 0u, 0, "text"),
                new LabDb.ColumnInfo("u", 99u, 3, "text"),
                new LabDb.ColumnInfo("s", 10u, 0, "session"),
                new LabDb.ColumnInfo("n", 0u, 0, "bigint"),
            ],
            [
                new object?[] { "canary", 1, "canary", "canary", "(1,canary)", 7L },
                new object?[] { null, 2, null, null, null, 0L },
            ],
            false);
        var origins = new Dictionary<(uint TableOid, short Attnum), (string Table, string Column)>
        {
            [(10u, (short)2)] = ("session", "token"),
            [(10u, (short)1)] = ("session", "id"),
        };

        DbQueryResult result = LabDb.BuildResult("betterauth", raw, origins);

        Assert.Equal(["t", "id", "x", "u", "s", "n"], result.Columns);
        Assert.Equal(2, result.RowCount);
        Dictionary<string, JsonElement> first = result.Rows[0];
        foreach (string masked in new[] { "t", "x", "u", "s" })
        {
            Assert.Equal(ColumnMask.Masked, first[masked].GetString());
        }

        Assert.Equal(1, first["id"].GetInt32());
        Assert.Equal(7, first["n"].GetInt64());
        foreach (string masked in new[] { "t", "x", "u", "s" })
        {
            Assert.Equal(JsonValueKind.Null, result.Rows[1][masked].ValueKind);
        }

        foreach (Dictionary<string, JsonElement> row in result.Rows)
        {
            foreach (JsonElement cell in row.Values)
            {
                Assert.DoesNotContain("canary", cell.ToString(), StringComparison.Ordinal);
            }
        }
    }
}
