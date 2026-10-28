using System.Text.Json;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Eval;

public sealed class ClauseOpsTests
{
    [Theory]
    [InlineData("eq", "5", "5.0", ClauseState.Held)]
    [InlineData("eq", "\"5\"", "5", ClauseState.NotHeld)]
    [InlineData("ne", "\"a\"", "1", ClauseState.Held)]
    [InlineData("ne", "\"a\"", "\"a\"", ClauseState.NotHeld)]
    [InlineData("gt", "3", "2", ClauseState.Held)]
    [InlineData("gt", "2", "2", ClauseState.NotHeld)]
    [InlineData("gte", "2", "2", ClauseState.Held)]
    [InlineData("lt", "3", "2", ClauseState.NotHeld)]
    [InlineData("lte", "2", "2", ClauseState.Held)]
    [InlineData("gt", "\"x\"", "2", ClauseState.NotHeld)]
    [InlineData("contains", "\"hello\"", "\"ell\"", ClauseState.Held)]
    [InlineData("contains", "5", "\"x\"", ClauseState.NotHeld)]
    [InlineData("not_contains", "\"hello\"", "\"zz\"", ClauseState.Held)]
    [InlineData("not_contains", "\"hello\"", "\"ell\"", ClauseState.NotHeld)]
    [InlineData("contains", "[1,2]", "2", ClauseState.Held)]
    [InlineData("matches", "\"abc1\"", "\"\\\\d\"", ClauseState.Held)]
    [InlineData("matches", "\"abc\"", "\"\\\\d\"", ClauseState.NotHeld)]
    [InlineData("len_gte", "\"abc\"", "3", ClauseState.Held)]
    [InlineData("len_gte", "[1]", "2", ClauseState.NotHeld)]
    [InlineData("len_gte", "7", "2", ClauseState.NotHeld)]
    [InlineData("approx", "10.04", "[10,0.05]", ClauseState.Held)]
    [InlineData("approx", "10.2", "[10,0.05]", ClauseState.NotHeld)]
    [InlineData("any", "\"b\"", "[\"a\",\"b\"]", ClauseState.Held)]
    [InlineData("any", "\"c\"", "[\"a\",\"b\"]", ClauseState.NotHeld)]
    [InlineData("truthy", "0", null, ClauseState.NotHeld)]
    [InlineData("truthy", "\"x\"", null, ClauseState.Held)]
    [InlineData("falsy", "\"\"", null, ClauseState.Held)]
    [InlineData("falsy", "null", null, ClauseState.Held)]
    [InlineData("exists", "null", null, ClauseState.Held)]
    [InlineData("min_rows", "3", "2", ClauseState.Held)]
    [InlineData("max_rows", "3", "2", ClauseState.NotHeld)]
    public void Op_ProducesExpectedState(string op, string target, string? value, ClauseState expected)
    {
        OpOutcome outcome = ClauseOps.Evaluate(op, Json(target), value is null ? null : Json(value), null);

        Assert.Equal(expected, outcome.State);
    }

    [Fact]
    public void Delta_ChangeMatchesExpected_IsHeld()
    {
        OpOutcome outcome = ClauseOps.Evaluate("delta", Json("7"), Json("2"), Json("5"));

        Assert.Equal(ClauseState.Held, outcome.State);
    }

    [Fact]
    public void Delta_WrongChange_IsNotHeld()
    {
        OpOutcome outcome = ClauseOps.Evaluate("delta", Json("7"), Json("3"), Json("5"));

        Assert.Equal(ClauseState.NotHeld, outcome.State);
    }

    [Fact]
    public void Delta_MissingBaseline_IsUnverified()
    {
        OpOutcome outcome = ClauseOps.Evaluate("delta", Json("7"), Json("2"), null);

        Assert.Equal(ClauseState.Unverified, outcome.State);
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("exists")]
    [InlineData("gt")]
    [InlineData("truthy")]
    [InlineData("matches")]
    public void MissingPointer_IsUnverified_NotHeld(string op)
    {
        OpOutcome outcome = ClauseOps.Evaluate(op, null, Json("1"), null);

        Assert.Equal(ClauseState.Unverified, outcome.State);
    }

    [Fact]
    public void MissingPointer_Absent_IsHeld()
    {
        OpOutcome outcome = ClauseOps.Evaluate("absent", null, null, null);

        Assert.Equal(ClauseState.Held, outcome.State);
    }

    [Fact]
    public void PresentPointer_Absent_IsNotHeld()
    {
        OpOutcome outcome = ClauseOps.Evaluate("absent", Json("1"), null, null);

        Assert.Equal(ClauseState.NotHeld, outcome.State);
    }

    [Fact]
    public void UnknownOp_IsUnverified()
    {
        OpOutcome outcome = ClauseOps.Evaluate("sort_of", Json("1"), Json("1"), null);

        Assert.Equal(ClauseState.Unverified, outcome.State);
    }

    [Fact]
    public void InvalidRegex_IsUnverified()
    {
        OpOutcome outcome = ClauseOps.Evaluate("matches", Json("\"a\""), Json("\"(\""), null);

        Assert.Equal(ClauseState.Unverified, outcome.State);
    }

    [Fact]
    public void JsonEquals_ObjectsCompareStructurally()
    {
        Assert.True(ClauseOps.JsonEquals(Json("{\"a\":1,\"b\":[true]}"), Json("{\"b\":[true],\"a\":1.0}")));
        Assert.False(ClauseOps.JsonEquals(Json("{\"a\":1}"), Json("{\"a\":1,\"b\":2}")));
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
}
