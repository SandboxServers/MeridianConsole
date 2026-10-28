using Dhadgar.Lab.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The C12 signature table: compiler-generated frames, route templates and fault kinds normalise to one stable text.
/// </summary>
public sealed class SignatureBuilderTests
{
    [Fact]
    public void Signature_CompilerFrames_Normalised()
    {
        Assert.Equal(
            "Dhadgar.Nodes.Enroll.HandleAsync",
            SignatureBuilder.NormalizeFrame("   at Dhadgar.Nodes.Enroll.<>c__DisplayClass3_0.<HandleAsync>b__0(Object x) in /src/Enroll.cs:line 42"));
        Assert.Equal(
            "Dhadgar.Nodes.Enroll.HandleAsync",
            SignatureBuilder.NormalizeFrame("Dhadgar.Nodes.Enroll.<HandleAsync>d__5.MoveNext() in /src/Enroll.cs:line 77"));
        Assert.Equal(
            "Dhadgar.Nodes.Store.Run",
            SignatureBuilder.NormalizeFrame("Dhadgar.Nodes.Store.Run`1[System.Int32](CancellationToken ct)+0x1f"));
    }

    [Fact]
    public void Signature_RouteDigitsAndGuids_Templated()
    {
        string route = SignatureBuilder.RouteOf(
            "/api/v1/servers/3fa85f64-5717-4562-b3fc-2c963f66afa6/tasks/42?verbose=1");

        Assert.Equal("/api/v{n}/servers/{id}/tasks/{n}", route);
    }

    [Fact]
    public void Signature_FaultKindsSorted_AndHttpTextMatchesTheTable()
    {
        var http = new HttpEvidence("nodes", 500, null, "post", "/api/v1/agents/enroll", "/api/v1/agents/enroll",
            ["pause", "kill"]);

        FailureSignature signature = SignatureBuilder.Http(http);

        Assert.Equal("http", signature.Kind);
        Assert.Equal("http:500|-|POST|/api/v1/agents/enroll|kill+pause", signature.Text);
        Assert.Equal("nodes", signature.Service);
    }

    [Fact]
    public void Signature_Id_IsSixteenLowercaseHexOfSha256()
    {
        string id = SignatureBuilder.SignatureId("inv:I1|nodes|check");

        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.Equal(id, SignatureBuilder.SignatureId("inv:I1|nodes|check"));
    }

    [Fact]
    public void BuildAll_ReturnsTheFiveHundredAndTheNotHeldClause()
    {
        RowEvidence row = Row(
            http:
            [
                new HttpEvidence("nodes", 500, "internal", "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", []),
                new HttpEvidence("nodes", 200, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", []),
            ],
            clauses:
            [
                new ClauseEvidence("nodes", "N1", "C-enroll-once", "NotHeld"),
                new ClauseEvidence("nodes", "N1", "C-enroll-held", "Held"),
            ]);

        IReadOnlyList<FailureSignature> signatures = SignatureBuilder.BuildAll(row);

        Assert.Equal(2, signatures.Count);
        Assert.Contains(signatures, s => s.Kind == "http" && s.Text.StartsWith("http:500|", StringComparison.Ordinal));
        Assert.Contains(signatures, s => s.Kind == "clause" && s.Text == "clause:nodes/N1/C-enroll-once|NotHeld");
    }

    [Fact]
    public void BuildAll_AllowedViolationsAndOptionalHistoryProduceNoSignature()
    {
        RowEvidence row = Row(
            violations:
            [
                new ViolationEvidence("I1", "nodes", "orphan", Allowed: true),
                new ViolationEvidence("I2", "nodes", "orphan", Allowed: false),
            ],
            history:
            [
                new HistoryViolationEvidence("enrollment", "E5", "enroll-lost-response", Required: false),
                new HistoryViolationEvidence("enrollment", "E1", "enroll-lost-response", Required: true),
            ]);

        IReadOnlyList<FailureSignature> signatures = SignatureBuilder.BuildAll(row);

        Assert.Equal(2, signatures.Count);
        Assert.Contains(signatures, s => s.Text == "inv:I2|nodes|orphan");
        Assert.Contains(signatures, s => s.Text == "hist:enrollment|E1|enroll-lost-response");
    }

    [Fact]
    public void Primary_PrefersTheExceptionThenTheFiveHundred()
    {
        RowEvidence row = Row(
            exceptions:
            [
                new ExceptionEvidence("nodes", "System.InvalidOperationException", ["Dhadgar.Nodes.Enroll.HandleAsync(Object o)"]),
            ],
            http:
            [
                new HttpEvidence("nodes", 503, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", []),
            ]);

        FailureSignature? primary = SignatureBuilder.Primary(row);

        Assert.NotNull(primary);
        Assert.Equal("exc", primary!.Kind);
        Assert.Equal(
            "exc:System.InvalidOperationException|Dhadgar.Nodes.Enroll.HandleAsync|-|-",
            primary.Text);
    }

    [Fact]
    public void Primary_NoFailure_IsNull()
    {
        Assert.Null(SignatureBuilder.Primary(Row()));
    }

    private static RowEvidence Row(
        IReadOnlyList<ExceptionEvidence>? exceptions = null,
        IReadOnlyList<HttpEvidence>? http = null,
        IReadOnlyList<ClauseEvidence>? clauses = null,
        IReadOnlyList<ViolationEvidence>? violations = null,
        IReadOnlyList<HistoryViolationEvidence>? history = null) =>
        new(
            "enrollment",
            "N1",
            exceptions ?? [],
            http ?? [],
            clauses ?? [],
            violations ?? [],
            history ?? [],
            []);
}
