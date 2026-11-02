using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The rows-filter dependency check (D-ML123, F110): a filter that drops a dependency or names an unknown row is refused.
/// </summary>
public sealed class RowFilterCheckTests
{
    private const string Chain = """
        schema = 1

        [section]
        id = "chain"
        title = "Chain"
        sequential = true
        requires = []
        allow_invariants = []

        [[row]]
        id = "a"
        title = "A"
        expected = "a holds"

        [[row]]
        id = "b"
        title = "B"
        expected = "b holds"
        depends_on = ["a"]

        [[row]]
        id = "c"
        title = "C"
        expected = "c holds"
        depends_on = ["b"]
        """;

    [Fact]
    public void Check_KeptRowWithoutItsDependency_IsRefusedNamingTheDependency()
    {
        UatSpec spec = LoadChain();

        Result result = RowFilterCheck.Check(spec, ["c"]);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument] rows filter keeps c without its dependency b", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WholeChain_Passes()
    {
        UatSpec spec = LoadChain();

        Result result = RowFilterCheck.Check(spec, ["a", "b", "c"]);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void Check_UnknownRow_IsNotFound()
    {
        UatSpec spec = LoadChain();

        Result result = RowFilterCheck.Check(spec, ["zz"]);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.NotFound] row zz is not in the spec", result.Error);
    }

    [Fact]
    public void Close_AddsTransitiveDependencies_InFileOrder()
    {
        UatSpec spec = LoadChain();

        IReadOnlyList<string> closed = RowFilterCheck.Close(spec, ["c"]);

        Assert.Equal(new[] { "a", "b", "c" }, closed);
        Result check = RowFilterCheck.Check(spec, closed);
        Assert.True(check.IsSuccess, check.Error);
    }

    [Fact]
    public void Close_UnknownRow_PassesThroughToNotFound()
    {
        UatSpec spec = LoadChain();

        IReadOnlyList<string> closed = RowFilterCheck.Close(spec, ["zz"]);

        Assert.Equal(new[] { "zz" }, closed);
        Result check = RowFilterCheck.Check(spec, closed);
        Assert.Equal("[Lab.NotFound] row zz is not in the spec", check.Error);
    }

    private static UatSpec LoadChain()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("chain.toml", Chain);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }
}
