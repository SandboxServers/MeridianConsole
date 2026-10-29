using Dhadgar.Lab.Twins;
using Xunit;

namespace Dhadgar.Lab.Tests.Twins;

public sealed class TwinSpecTests
{
    private const string Valid = """
        schema = 1
        id = "enroll-latency-invariance"
        relation = "equal_outcome"
        seed = 42
        [base]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        [variant]
        spec = "catalog/twin-enroll.toml"
        rows = ["enroll-once"]
        faults = [{ tool = "fault_toxic_add", args = { proxy = "nodes-postgres", toxic = "latency", stream = "downstream", attributes = { latency = 1500 } } }]
        [[compare]]
        pointer = "/rows/enroll-once/steps/enroll/status"
        [[compare]]
        pointer = "/rows/enroll-once/grade"
        """;

    [Fact]
    public void Load_ValidEqualOutcome_Parses()
    {
        var result = TwinSpecLoader.Load(Valid);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("enroll-latency-invariance", result.Value!.Id);
        Assert.Equal(TwinRelation.EqualOutcome, result.Value.Relation);
        Assert.Equal(42UL, result.Value.Seed);
        Assert.Single(result.Value.Variant.Faults);
        Assert.Equal("fault_toxic_add", result.Value.Variant.Faults[0].Tool);
        Assert.Equal(2, result.Value.Compares.Count);
    }

    [Fact]
    public void Load_UnknownTopLevelKey_Refused()
    {
        var result = TwinSpecLoader.Load(Valid + "\nextra = 1\n");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Load_DisjointOutsideIsolated_Refused()
    {
        string text = Valid.Replace("[[compare]]\npointer = \"/rows/enroll-once/grade\"", "[[compare]]\nvariant_pointers = [\"/a\", \"/b\"]\nop = \"disjoint\"", StringComparison.Ordinal);

        var result = TwinSpecLoader.Load(text);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Load_DisjointUnderIsolated_Parses()
    {
        string text = Valid
            .Replace("relation = \"equal_outcome\"", "relation = \"isolated\"", StringComparison.Ordinal)
            .Replace("[[compare]]\npointer = \"/rows/enroll-once/grade\"", "[[compare]]\nvariant_pointers = [\"/a\", \"/b\"]\nop = \"disjoint\"", StringComparison.Ordinal);

        var result = TwinSpecLoader.Load(text);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("disjoint", result.Value!.Compares[1].Op);
    }

    [Fact]
    public void Load_NonFaultTool_Refused()
    {
        string text = Valid.Replace("fault_toxic_add", "fault_list_targets", StringComparison.Ordinal);

        Assert.True(TwinSpecLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_FaultOnBaseWorld_Refused()
    {
        string text = Valid.Replace("[base]\nspec = \"catalog/twin-enroll.toml\"\nrows = [\"enroll-once\"]",
            "[base]\nspec = \"catalog/twin-enroll.toml\"\nrows = [\"enroll-once\"]\nfaults = [{ tool = \"fault_kill\", args = {} }]", StringComparison.Ordinal);

        Assert.True(TwinSpecLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_MalformedPointer_Refused()
    {
        string text = Valid.Replace("/rows/enroll-once/grade", "rows/enroll-once/grade", StringComparison.Ordinal);

        Assert.True(TwinSpecLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_BadId_Refused()
    {
        string text = Valid.Replace("enroll-latency-invariance", "Enroll_Latency", StringComparison.Ordinal);

        Assert.True(TwinSpecLoader.Load(text).IsFailure);
    }

    [Fact]
    public void CheckBaseRows_MissingRow_Fails()
    {
        var spec = TwinSpecLoader.Load(Valid).Value!;

        Assert.True(TwinSpecLoader.CheckBaseRows(spec, ["enroll-once"]).IsSuccess);
        Assert.True(TwinSpecLoader.CheckBaseRows(spec, ["other-row"]).IsFailure);
    }
}
