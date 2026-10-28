using System.Text.Json;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Eval;

public sealed class ClauseEvaluatorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly SecretVault _vault = new();

    public void Dispose() => _vault.Dispose();

    [Fact]
    public async Task HeldOnFirstCheck_ReportsOneAttempt()
    {
        var source = new FakeSource("db", Ok("{\"rows\":[{\"used_at\":null}],\"row_count\":1}"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "exists", "/rows/0/used_at"));

        Assert.Equal(ClauseState.Held, result.State);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task MissingPointer_IsUnverified_NotHeld()
    {
        var source = new FakeSource("db", Ok("{\"rows\":[]}"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "exists", "/rows/0/used_at"));

        Assert.Equal(ClauseState.Unverified, result.State);
    }

    [Fact]
    public async Task SinceFaultHeal_NotHealed_IsUnverified_AndTheSourceIsNotRead()
    {
        var source = new FakeSource("db", Ok("{\"n\":1}"));
        UatClause clause = Clause("db", "eq", "/n", since: "fault_heal:pg-down", value: "1");

        ClauseEvaluation result = await Evaluate(source, clause);

        Assert.Equal(ClauseState.Unverified, result.State);
        Assert.Equal("fault pg-down not healed", result.Error);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task SourceError_IsUnverified_WithTheErrorText_AndIsNotPolled()
    {
        var source = new FakeSource("db", Result<JsonElement>.Failure("[Lab.Unavailable] database is down"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "exists", "/x", timeoutMs: 2000, pollMs: 1));

        Assert.Equal(ClauseState.Unverified, result.State);
        Assert.Equal("[Lab.Unavailable] database is down", result.Error);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Polling_HoldsOnALaterCheck()
    {
        var source = new FakeSource("db", Ok("{\"n\":0}"), Ok("{\"n\":0}"), Ok("{\"n\":1}"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "gte", "/n", value: "1", timeoutMs: 5000, pollMs: 1));

        Assert.Equal(ClauseState.Held, result.State);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Timeout_IsNotHeld()
    {
        var source = new FakeSource("db", Ok("{\"n\":0}"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "gte", "/n", value: "1", timeoutMs: 30, pollMs: 5));

        Assert.Equal(ClauseState.NotHeld, result.State);
        Assert.True(result.Attempts > 1);
    }

    [Fact]
    public async Task Delta_UsesTheBaselineCapturedBeforeTheRow()
    {
        var source = new FakeSource("db", Ok("{\"n\":5}"), Ok("{\"n\":7}"));
        ClauseEvaluator evaluator = Evaluator(source);
        UatVars vars = Vars();
        ClauseWindow window = Window();
        UatClause clause = Clause("db", "delta", "/n", value: "2");

        Result<JsonElement> baseline = await evaluator.CaptureBaselineAsync(clause, vars, window, CancellationToken.None);
        ClauseEvaluation result = await evaluator.EvaluateAsync(clause, vars, window, baseline, CancellationToken.None);

        Assert.Equal(ClauseState.Held, result.State);
    }

    [Fact]
    public async Task Delta_WithoutBaseline_IsUnverified()
    {
        var source = new FakeSource("db", Ok("{\"n\":7}"));
        ClauseEvaluation result = await Evaluate(source, Clause("db", "delta", "/n", value: "2"));

        Assert.Equal(ClauseState.Unverified, result.State);
    }

    [Fact]
    public async Task UnknownSource_IsUnverified()
    {
        var source = new FakeSource("db", Ok("{}"));
        ClauseEvaluation result = await Evaluate(source, Clause("nosuch", "exists", "/x"));

        Assert.Equal(ClauseState.Unverified, result.State);
        Assert.Equal("unknown source nosuch", result.Error);
    }

    [Fact]
    public async Task InvariantSource_BeforeL21_IsUnverified()
    {
        var source = new FakeSource("db", Ok("{}"));
        ClauseEvaluation result = await Evaluate(source, Clause("invariant", "absent", "/violations"));

        Assert.Equal(ClauseState.Unverified, result.State);
        Assert.Equal("invariants arrive in L21", result.Error);
    }

    [Fact]
    public async Task HumanClause_IsPending_AndNothingIsRead()
    {
        var source = new FakeSource("db", Ok("{}"));
        ClauseEvaluation result = await Evaluate(source, Clause("human", "truthy", null));

        Assert.Equal(ClauseState.Pending, result.State);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task ValueVariables_AreSubstitutedBeforeTheOp()
    {
        var vars = Vars();
        Assert.True(vars.Capture("token_id", JsonSerializer.Deserialize<JsonElement>("{\"id\":\"abc\"}"), "/id").IsSuccess);
        var source = new FakeSource("db", Ok("{\"x\":\"abc\"}"));
        ClauseEvaluation result = await Evaluator(source).EvaluateAsync(
            Clause("db", "eq", "/x", value: "\"${token_id}\""),
            vars,
            Window(),
            null,
            CancellationToken.None);

        Assert.Equal(ClauseState.Held, result.State);
    }

    [Fact]
    public async Task StepSource_AssertsAnExpectedErrorCode()
    {
        var steps = new Dictionary<string, StepOutcome>(StringComparer.Ordinal)
        {
            ["mint"] = new(Ok: false, Error: "conflict", ErrorCode: "Lab.Conflict", DurationMs: 5, Result: null),
        };
        var window = new ClauseWindow(Start, Start, new Dictionary<string, DateTimeOffset>(), new Dictionary<string, DateTimeOffset>(), steps);
        var source = new StepSource();

        ClauseEvaluation result = await Evaluator(source).EvaluateAsync(
            Clause("step", "eq", "/error_code", value: "\"Lab.Conflict\"", fields: new() { ["step"] = Json("\"mint\"") }),
            Vars(),
            window,
            null,
            CancellationToken.None);

        Assert.Equal(ClauseState.Held, result.State);
    }

    [Fact]
    public async Task LogSource_CountsMatchingLines()
    {
        var ring = new LogRing();
        ring.Add(new LogLine(Start.AddMinutes(1), "Information", "Dhadgar.Nodes", null, "Hub connected node-1"));
        ring.Add(new LogLine(Start.AddMinutes(2), "Information", "Dhadgar.Nodes", null, "heartbeat"));
        var source = new LogSource(name => name == "api" ? ring : null);

        ClauseEvaluation result = await Evaluator(source).EvaluateAsync(
            Clause("log", "eq", "/count", value: "1", fields: new() { ["process"] = Json("\"api\""), ["contains"] = Json("\"connected\"") }),
            Vars(),
            Window(),
            null,
            CancellationToken.None);

        Assert.Equal(ClauseState.Held, result.State);
    }

    private async Task<ClauseEvaluation> Evaluate(IClauseSource source, UatClause clause) =>
        await Evaluator(source).EvaluateAsync(clause, Vars(), Window(), null, CancellationToken.None);

    private ClauseEvaluator Evaluator(IClauseSource source) =>
        new(new[] { source }, new Redactor(_vault), TimeProvider.System);

    private static UatVars Vars() => new("run1", "row1", "section1", 42, null);

    private static ClauseWindow Window() =>
        new(Start, Start, new Dictionary<string, DateTimeOffset>(), new Dictionary<string, DateTimeOffset>(), new Dictionary<string, StepOutcome>());

    private static UatClause Clause(
        string source,
        string op,
        string? pointer,
        string since = "row_start",
        string? value = null,
        int timeoutMs = 0,
        int pollMs = 500,
        Dictionary<string, JsonElement>? fields = null) =>
        new(
            Id: "c1",
            Text: "clause",
            Source: source,
            Op: op,
            Value: value is null ? null : Json(value),
            Pointer: pointer,
            Since: since,
            TimeoutMs: timeoutMs,
            PollMs: pollMs,
            Required: true,
            SourceFields: fields ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static Result<JsonElement> Ok(string json) => Result<JsonElement>.Success(Json(json));

    /// <summary>A source that answers each read from a fixed list, repeating the last answer.</summary>
    private sealed class FakeSource : IClauseSource
    {
        private readonly Result<JsonElement>[] _responses;

        public FakeSource(string name, params Result<JsonElement>[] responses)
        {
            Name = name;
            _responses = responses;
        }

        public string Name { get; }

        public int Reads { get; private set; }

        public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
        {
            int index = Math.Min(Reads, _responses.Length - 1);
            Reads++;
            return Task.FromResult(_responses[index]);
        }
    }
}
