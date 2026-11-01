using Dhadgar.Lab.Hunt;
using Dhadgar.Lab.Tools.Hunt;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Hunt;

/// <summary>
/// The writer-parser seam (F116): an op the hunter writes into <c>repro.toml</c> parses back to the same op, for every kind.
/// </summary>
public sealed class ReproRoundTripTests
{
    [Theory]
    [InlineData(HuntOpKind.Mint)]
    [InlineData(HuntOpKind.Enroll)]
    [InlineData(HuntOpKind.EnrollAgain)]
    [InlineData(HuntOpKind.PgDown)]
    [InlineData(HuntOpKind.PgUp)]
    [InlineData(HuntOpKind.RestartNodes)]
    public void EveryOpKind_WrittenThenParsed_IsTheSameOp(HuntOpKind kind)
    {
        int token = kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain ? 3 : 0;
        var op = new HuntOp(kind, token);

        Result<HuntModelTool.ReproRecord> parsed = HuntModelTool.ReproParser.Parse(Repro([op]));

        Assert.True(parsed.IsSuccess, parsed.Error);
        HuntOp read = Assert.Single(parsed.Value!.Ops);
        Assert.Equal(kind, read.Kind);
        Assert.Equal(op, read);
    }

    [Fact]
    public void AllKindsInOneRepro_ParseInOrder()
    {
        List<HuntOp> ops = Enum.GetValues<HuntOpKind>()
            .Select(kind => new HuntOp(kind, kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain ? 7 : 0))
            .ToList();

        Result<HuntModelTool.ReproRecord> parsed = HuntModelTool.ReproParser.Parse(Repro(ops));

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(ops, parsed.Value!.Ops);
    }

    /// <summary>A minimal repro text, with the ops line built the way the hunter's writer builds it (ModelHunter.WriteReproAsync).</summary>
    private static string Repro(IReadOnlyList<HuntOp> ops)
    {
        string opsLine = string.Join(", ", ops.Select(op => "\"" + op + "\""));
        return "kind = \"hunt\"\n"
            + "model = \"enrollment\"\n"
            + "run_seed = 20261028\n"
            + "cscheck_seed = \"0\"\n"
            + "build_id = \"build-1\"\n"
            + "ops = [" + opsLine + "]\n"
            + "first_diverging_op = 0\n"
            + "detail = \"op 0: round trip\"\n";
    }
}
