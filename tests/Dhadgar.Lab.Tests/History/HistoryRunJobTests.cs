using System.Text.Json;
using Dhadgar.Lab.Barrier;
using Dhadgar.Lab.History;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.History.Rules;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The history run job: the grading of a check (unknown is never a pass, a required violation fails the row), the check's
/// merge of the model and the E-rules, and the run's refusal paths before any environment step runs.
/// </summary>
public sealed class HistoryRunJobTests : IDisposable
{
    private readonly SecretVault _vault = new();
    private readonly HttpClientHandler _handler = new();

    public void Dispose()
    {
        _handler.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public void Grade_RequiredViolation_IsFail()
    {
        var check = new CheckResult(
            LinearizabilityChecker.Violation,
            [new HistoryViolation("linearizability", ["op-1"], "no linearization")],
            null);

        Assert.Equal(RowGrade.Fail, HistoryRunJob.Grade(check));
    }

    [Fact]
    public void Grade_Unknown_IsUnverified_NeverPass()
    {
        var check = new CheckResult(LinearizabilityChecker.Unknown, [], null, "budget exhausted");

        Assert.Equal(RowGrade.Unverified, HistoryRunJob.Grade(check));
    }

    [Fact]
    public void Grade_Linearizable_WithOnlyNonRequiredViolation_IsPass()
    {
        var check = new CheckResult(
            LinearizabilityChecker.Linearizable,
            [new HistoryViolation("E5", ["op-2"], "retry orphan", Required: false)],
            null);

        Assert.Equal(RowGrade.Pass, HistoryRunJob.Grade(check));
    }

    [Fact]
    public void Evaluate_EmptyHistory_IsLinearizable()
    {
        var history = new Dhadgar.Lab.History.History("run-1", "enroll-race", [], EmptyState());

        CheckResult check = HistoryRunJob.Evaluate(history, minimise: true);

        Assert.Equal(LinearizabilityChecker.Linearizable, check.Verdict);
        Assert.Null(check.Minimal);
        Assert.Equal(RowGrade.Pass, HistoryRunJob.Grade(check));
    }

    [Fact]
    public async Task RunAsync_ClientsOutOfRange_RefusedBeforeAnyEnvironmentStep()
    {
        var env = new RecordingEnvironment();
        HistoryRunJob job = NewJob(env);
        HistoryRunRequest request = NewRequest(clients: 9);

        Result<HistoryRunResult> result = await job.RunAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, env.Calls);
    }

    [Fact]
    public async Task RunAsync_ResetFailure_PropagatesAndMintsNothing()
    {
        var env = new RecordingEnvironment { ResetError = "[Lab.Unavailable] the database is down" };
        HistoryRunJob job = NewJob(env);

        Result<HistoryRunResult> result = await job.RunAsync(NewRequest(clients: 4), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("database is down", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, env.Calls);
    }

    [Fact]
    public async Task RunAsync_OneClient_IsAccepted()
    {
        var env = new RecordingEnvironment { ResetError = "[Lab.Unavailable] stop after the reset" };
        HistoryRunJob job = NewJob(env);

        Result<HistoryRunResult> result = await job.RunAsync(NewRequest(clients: 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("between", result.Error, StringComparison.Ordinal);
        Assert.True(env.Calls >= 1);
    }

    [Fact]
    public async Task RunAsync_ZeroClients_StillRefused()
    {
        var env = new RecordingEnvironment();
        HistoryRunJob job = NewJob(env);

        Result<HistoryRunResult> result = await job.RunAsync(NewRequest(clients: 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, env.Calls);
    }

    [Fact]
    public async Task RunAsync_CancelledAfterInject_StillHealsOnce()
    {
        using var cts = new CancellationTokenSource();
        var env = new RecordingEnvironment { CancelOnInject = cts };
        var job = new HistoryRunJob(env, new OpenSink(), new Redactor(_vault), _vault, _handler, TimeProvider.System);

        try
        {
            _ = await job.RunAsync(NewRequest(clients: 2), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // The cancelled run may surface the cancellation; the heal must still have run.
        }

        Assert.Equal(1, env.HealCalls);
    }

    [Fact]
    public async Task RunAsync_ClientPhaseLongerThanCleanupCap_StillHealsOnceAndEndsRun()
    {
        // The cleanup deadline is a wall-clock CancellationTokenSource, not the TimeProvider, so this test waits in real time
        // for longer than the 30 s cleanup cap. The delay is placed before the client phase, which runs inside the same try.
        var env = new RecordingEnvironment { InjectDelay = TimeSpan.FromSeconds(31) };
        var sink = new OpenSink();
        var job = new HistoryRunJob(env, sink, new Redactor(_vault), _vault, _handler, TimeProvider.System);

        _ = await job.RunAsync(NewRequest(clients: 2), CancellationToken.None);

        Assert.Equal(1, env.HealCalls);
        Assert.False(env.HealCancelled);
        Assert.Equal(1, sink.EndCalls);
        Assert.False(sink.EndCancelled);
    }

    [Fact]
    public void RerunClientCounts_HalvesToTheFloor_AndStopsAtTheCap()
    {
        Assert.Equal(new[] { 4, 2 }, HistoryRunJob.RerunClientCounts(8, 20).ToArray());
        Assert.Equal(new[] { 4 }, HistoryRunJob.RerunClientCounts(8, 1).ToArray());
        Assert.Empty(HistoryRunJob.RerunClientCounts(1, 20));
    }

    [Fact]
    public void BuildRules_RetryScenario_ArmsPassForRetryOps()
    {
        int[] order = [1, 2];
        var firstOps = new Dictionary<int, string> { [1] = "run/client:1/1", [2] = "run/client:2/1" };
        var retryOps = new Dictionary<int, string> { [1] = "run/client:1/2", [2] = "run/client:2/2" };

        IReadOnlyList<BarrierRule> rules = HistoryRunJob.BuildRules(order, firstOps, retryOps, hold: false);

        Assert.Equal(4, rules.Count);
        Assert.Equal(new[] { "run/client:1/2", "run/client:2/2" }, rules.Where(r => r.Action == BarrierAction.Pass).Select(r => r.OpId).ToArray());
    }

    [Fact]
    public void BuildSnapshot_DbQueryRows_SatisfyEnrollmentRules()
    {
        JsonElement snapshot = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.BuildSnapshot(
            EnrollmentFixtures.TokenId,
            DbRows("{\"id\":\"token-1\",\"revoked\":false,\"used_at\":\"2026-01-01T00:00:00Z\",\"used_by_node_id\":\"node-1\"}"),
            DbRows("{\"id\":\"node-1\",\"name\":\"lab-abc123-c1-1\",\"active_certs\":1}"));
        Dhadgar.Lab.History.History history = EnrollmentFixtures.Build(
            snapshot,
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-abc123-c1-1"),
            EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "node-1"));

        Assert.Empty(EnrollmentRules.Evaluate(history, HistoryRunJob.IdempotentRetry));
        CheckResult check = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(HistoryRunJob.IdempotentRetry), TimeSpan.FromSeconds(10));
        Assert.Equal(LinearizabilityChecker.Linearizable, check.Verdict);
    }

    [Fact]
    public void BuildSnapshot_UsedTokenWithoutUsedByNode_RaisesRequiredViolation()
    {
        JsonElement snapshot = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.BuildSnapshot(
            EnrollmentFixtures.TokenId,
            DbRows("{\"id\":\"token-1\",\"revoked\":false,\"used_at\":\"2026-01-01T00:00:00Z\",\"used_by_node_id\":null}"),
            DbRows("{\"id\":\"node-1\",\"name\":\"lab-abc123-c1-1\",\"active_certs\":1}"));
        Dhadgar.Lab.History.History history = EnrollmentFixtures.Build(
            snapshot,
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-abc123-c1-1"),
            EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "node-1"));

        CheckResult check = HistoryRunJob.Evaluate(history, minimise: false);

        Assert.Contains(check.Violations, v => v.Required);
        Assert.Equal(RowGrade.Fail, HistoryRunJob.Grade(check));
    }

    [Fact]
    public void FailureText_KeepsTheNestedMessage()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"error\":{\"code\":\"Lab.Failed\",\"message\":\"nodes token mint failed: PostgresException\"}}");

        string text = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.FailureText("nodes_token_mint", doc.RootElement);

        Assert.Equal("[Lab.Failed] nodes_token_mint failed: nodes token mint failed: PostgresException", text);
    }

    [Fact]
    public void FailureText_NoMessage_KeepsCodeOnly()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"error\":{\"code\":\"Lab.Failed\"}}");

        string text = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.FailureText("nodes_token_mint", doc.RootElement);

        Assert.Equal("[Lab.Failed] nodes_token_mint failed", text);
    }

    [Fact]
    public void FailureText_NotAnEnvelope_IsUnavailable()
    {
        using JsonDocument doc = JsonDocument.Parse("[]");

        string text = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.FailureText("nodes_token_mint", doc.RootElement);

        Assert.Equal("[Lab.Unavailable] nodes_token_mint failed", text);
    }

    [Fact]
    public void FailureText_LongMessage_IsCapped()
    {
        string longMessage = new('x', 2000);
        using JsonDocument doc = JsonDocument.Parse("{\"error\":{\"code\":\"Lab.Failed\",\"message\":\"" + longMessage + "\"}}");

        string text = Dhadgar.Lab.Tools.DispatchedHistoryEnvironment.FailureText("nodes_token_mint", doc.RootElement);

        const string Prefix = "[Lab.Failed] nodes_token_mint failed: ";
        Assert.Equal(Prefix.Length + 512, text.Length);
        Assert.StartsWith(Prefix, text, StringComparison.Ordinal);
        Assert.EndsWith(new string('x', 512), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_TwoOkOneToken_IsViolationAndGradesFail()
    {
        Dhadgar.Lab.History.History history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(used: true, usedByNodeId: "node-1", ("node-1", "lab-abc123-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-abc123-c1-1"),
            EnrollmentFixtures.Invoke(2, "op-2", "client:2", "lab-abc123-c2-1"),
            EnrollmentFixtures.Complete(3, "op-1", "client:1", OpOutcome.Ok, 201, "node-1"),
            EnrollmentFixtures.Complete(4, "op-2", "client:2", OpOutcome.Ok, 201, "node-2"));

        CheckResult check = HistoryRunJob.Evaluate(history, minimise: true);

        Assert.Equal(LinearizabilityChecker.Violation, check.Verdict);
        Assert.Equal(RowGrade.Fail, HistoryRunJob.Grade(check));
        Assert.NotNull(check.Minimal);
    }

    [Fact]
    public void FromObservations_StragglerInvoke_IsUnknown_NeverPasses()
    {
        Dhadgar.Lab.History.History history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(used: false, usedByNodeId: null, ("node-1", "lab-abc123-c1-1", 0)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-abc123-c1-1"));

        CheckResult check = HistoryRunJob.Evaluate(history, minimise: false);

        Assert.Single(history.Ops);
        Assert.NotEqual(RowGrade.Pass, HistoryRunJob.Grade(check));
    }

    private static JsonElement DbRows(string row) => JsonDocument.Parse("{\"rows\":[" + row + "]}").RootElement.Clone();

    private HistoryRunJob NewJob(RecordingEnvironment env) =>
        new(env, new ThrowingSink(), new Redactor(_vault), _vault, _handler, TimeProvider.System);

    private static HistoryRunRequest NewRequest(int clients) => new(
        "9f2c4a1e-7b00-4c11-8d22-000000000042",
        "scratch-folder",
        new HistoryScenario("enroll-race", 4, HistoryRetry.None, HistoryFirstOpAction.HoldRequest, []),
        clients,
        42UL,
        Minimise: true);

    private static JsonElement EmptyState() => JsonDocument.Parse("{\"token\":null,\"nodes\":[]}").RootElement.Clone();

    private sealed class RecordingEnvironment : IHistoryEnvironment
    {
        public int Calls { get; private set; }

        public string? ResetError { get; init; }

        /// <summary>Cancelled inside <see cref="InjectFaultsAsync"/>, to test the heal on a cancelled run.</summary>
        public CancellationTokenSource? CancelOnInject { get; init; }

        public int HealCalls { get; private set; }

        /// <summary>Whether the heal was handed an already-cancelled token (a real heal cannot run on one).</summary>
        public bool HealCancelled { get; private set; }

        /// <summary>A delay inside <see cref="InjectFaultsAsync"/>, to make the run's client phase outlast the cleanup cap.</summary>
        public TimeSpan? InjectDelay { get; init; }

        public Task<Result> ResetDataAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(ResetError is null ? Result.Success() : Result.Failure(ResetError));
        }

        public Task<Result<HistoryToken>> MintTokenAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result<HistoryToken>.Success(new HistoryToken("tok-1", "token-ref-1")));
        }

        public async Task<Result> InjectFaultsAsync(IReadOnlyList<UatFault> faults, CancellationToken ct)
        {
            Calls++;
            if (CancelOnInject is not null)
            {
                await CancelOnInject.CancelAsync().ConfigureAwait(false);
            }

            if (InjectDelay is { } delay)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            return Result.Success();
        }

        public Task<Result> HealFaultsAsync(CancellationToken ct)
        {
            Calls++;
            HealCalls++;
            HealCancelled = ct.IsCancellationRequested;
            return Task.FromResult(ct.IsCancellationRequested
                ? Result.Failure("[Lab.Unavailable] the heal was cancelled")
                : Result.Success());
        }

        public Task<Result<JsonElement>> SnapshotAsync(string tokenId, string run6, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result<JsonElement>.Success(EmptyState()));
        }
    }

    /// <summary>A sink that accepts the run's steps (no observations are read back), for the cancelled-run test.</summary>
    private sealed class OpenSink : ILabObservationSink
    {
        private long _seq;

        public int EndCalls { get; private set; }

        /// <summary>Whether the run was ended with an already-cancelled token.</summary>
        public bool EndCancelled { get; private set; }

        public Result BeginRun(string runId, string runFolder) => Result.Success();

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct)
        {
            EndCalls++;
            EndCancelled = ct.IsCancellationRequested;
            return Task.FromResult(Result<long>.Success(0));
        }

        public OpId NextOp(string runId, string actor) => new(runId, actor, Interlocked.Increment(ref _seq));

        public Result<long> Append(LabObservation draft) => Result<long>.Success(1);

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => [];
    }

    /// <summary>A sink that must not be reached by the refusal paths the tests cover.</summary>
    private sealed class ThrowingSink : ILabObservationSink
    {
        public Result BeginRun(string runId, string runFolder) => Result.Success();

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => throw new NotSupportedException();

        public OpId NextOp(string runId, string actor) => throw new NotSupportedException();

        public Result<long> Append(LabObservation draft) => throw new NotSupportedException();

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => throw new NotSupportedException();
    }
}
