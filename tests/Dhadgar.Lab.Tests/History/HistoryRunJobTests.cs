using System.Text.Json;
using Dhadgar.Lab.History;
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

        public Task<Result> InjectFaultsAsync(IReadOnlyList<UatFault> faults, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> HealFaultsAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<JsonElement>> SnapshotAsync(string tokenId, string run6, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result<JsonElement>.Success(EmptyState()));
        }
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
