using Dhadgar.Lab.Fuzz;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Fuzz;

public sealed class FuzzOracleTests
{
    private const string Problem = "{\"type\":\"https://errors.meridianconsole.com/validation\",\"title\":\"Bad request\",\"status\":400,\"errorCode\":\"invalid_token\"}";

    private static readonly FuzzCase SomeCase = new(7, "drop-property", ReadOnlyMemory<byte>.Empty, "application/json");

    [Fact]
    public void Oracle_500_IsFailure()
    {
        FuzzObservation observation = new(500, "application/problem+json", Problem, 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, KnownIssues.Empty, []);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("server error", StringComparison.Ordinal));
    }

    [Fact]
    public void Oracle_500_IsNeverKnown_EvenWithTheShippedIssues()
    {
        // A 5xx is never matched by KI-F69's pattern (the 4xx one), so it stays a failure against the shipped list.
        FuzzObservation observation = new(500, "text/plain", "boom", 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, LoadShipped(), []);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
    }

    [Fact]
    public void Oracle_400WithoutProblemDetails_Fails()
    {
        // Reverting the error-contract rule makes this fail: a 400 with a plain body breaks F29.
        FuzzObservation observation = new(400, "text/plain", "bad", 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, KnownIssues.Empty, []);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
    }

    [Fact]
    public void Oracle_400ProblemDetails_IsOk()
    {
        FuzzObservation observation = new(400, "application/problem+json; charset=utf-8", Problem, 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, KnownIssues.Empty, []);

        Assert.Equal(FuzzVerdictKind.Ok, verdict.Kind);
        Assert.Empty(verdict.Reasons);
    }

    [Fact]
    public void Oracle_429_IsThrottledNotFailure()
    {
        FuzzObservation observation = new(429, "text/plain", "slow down", 5, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, KnownIssues.Empty, []);

        Assert.Equal(FuzzVerdictKind.Throttled, verdict.Kind);
        Assert.Empty(verdict.Signatures);
    }

    [Fact]
    public void Oracle_KiF69Signature_IsKnown()
    {
        // The shipped KI-F69-HTTP pattern excuses a 4xx without problem details on the enroll route, and only that.
        FuzzObservation observation = new(400, "text/plain", "bad", 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, LoadShipped(), []);

        Assert.Equal(FuzzVerdictKind.Known, verdict.Kind);
        Assert.Equal("KI-F69-HTTP", verdict.KnownIssueId);
    }

    [Fact]
    public void Oracle_LatencyAtTenSeconds_Fails()
    {
        FuzzObservation observation = new(400, "application/problem+json", Problem, 10_000, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, KnownIssues.Empty, []);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("latency", StringComparison.Ordinal));
    }

    [Fact]
    public void Oracle_SecretInResponse_IsFailureAndNeverKnown()
    {
        string leaking = "{\"type\":\"t\",\"title\":\"x\",\"status\":400,\"detail\":\"vault-value-7f3a\"}";
        FuzzObservation observation = new(400, "application/problem+json", leaking, 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, LoadShipped(), ["vault-value-7f3a"]);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Judge_LeakCheckTrue_IsALeak_AndTheFindingOmitsTheBody()
    {
        string body = "{\"type\":\"t\",\"title\":\"x\",\"status\":400,\"detail\":\"opaque-token-9c1e\"}";
        FuzzObservation observation = new(400, "application/problem+json", body, 40, null);

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, LoadShipped(), [],
            text => text.Contains("opaque-token-9c1e", StringComparison.Ordinal));

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(verdict.Reasons, r => r.Contains("opaque-token-9c1e", StringComparison.Ordinal));
        Assert.DoesNotContain(verdict.Signatures, s => s.Text.Contains("opaque-token-9c1e", StringComparison.Ordinal));
    }

    [Fact]
    public void Oracle_NoResponse_IsFailure()
    {
        FuzzObservation observation = new(0, null, string.Empty, 30_000, "timed out");

        FuzzVerdict verdict = FuzzOracle.Judge(FuzzTargets.Enroll, SomeCase, observation, LoadShipped(), []);

        Assert.Equal(FuzzVerdictKind.Failure, verdict.Kind);
    }

    [Fact]
    public void RowId_IsFuzzTargetAndIndex()
    {
        Assert.Equal("fuzz/enroll/7", FuzzOracle.RowId(FuzzTargets.Enroll, SomeCase));
    }

    private static KnownIssues LoadShipped()
    {
        string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
            "docs", "guides", "uat-specs", "known-issues.toml");
        Result<KnownIssues> loaded = KnownIssues.Load(path);
        Assert.True(loaded.IsSuccess, loaded.Error);
        return loaded.Value!;
    }
}
