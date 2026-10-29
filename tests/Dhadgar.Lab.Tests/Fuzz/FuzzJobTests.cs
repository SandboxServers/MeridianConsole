using System.Text.Json.Nodes;
using Dhadgar.Lab.Fuzz;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Fuzz;

public sealed class FuzzJobTests
{
    private const ulong Seed = 20261028UL;

    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FuzzJob_500ForOneCase_IsOneFailingRow()
    {
        var transport = new FakeTransport(fuzzCase => fuzzCase.Index == 5 ? Probe(500) : Probe(200));
        var job = new FuzzJob(transport, new GatewayPacer(new FakeTimeProvider(Start)), new FakeTimeProvider(Start),
            KnownIssues.Empty, []);

        Result<JobOutcome> outcome = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 10, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        JsonArray failures = (JsonArray)outcome.Value.Result!["failures"]!;
        Assert.Single(failures);
        Assert.Equal("fuzz/enroll/5", failures[0]!["id"]!.GetValue<string>());
        Assert.Equal("http:500|-|POST|/api/v1/agents/enroll|none", failures[0]!["signatures"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task FuzzJob_ReplayIndex_ReproducesTheSameRow()
    {
        var transport = new FakeTransport(fuzzCase => fuzzCase.Index == 5 ? Probe(500) : Probe(200));
        var job = new FuzzJob(transport, new GatewayPacer(new FakeTimeProvider(Start)), new FakeTimeProvider(Start),
            KnownIssues.Empty, []);

        Result<JobOutcome> full = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 10, CancellationToken.None);
        Result<JobOutcome> replay = await job.RunAsync(Seed, FuzzTargets.Enroll, 5, 1, CancellationToken.None);

        JsonNode? fullRow = ((JsonArray)full.Value!.Result!["failures"]!)[0];
        JsonNode? replayRow = ((JsonArray)replay.Value!.Result!["failures"]!)[0];
        Assert.Equal("fail", replay.Value.Overall);
        Assert.Equal(fullRow!["id"]!.GetValue<string>(), replayRow!["id"]!.GetValue<string>());
        Assert.Equal(fullRow["mutation"]!.GetValue<string>(), replayRow["mutation"]!.GetValue<string>());
        Assert.Equal(5, replayRow["index"]!.GetValue<int>());
    }

    [Fact]
    public async Task FuzzJob_CleanRun_Passes()
    {
        var transport = new FakeTransport(_ => Probe(200));
        var job = new FuzzJob(transport, new GatewayPacer(new FakeTimeProvider(Start)), new FakeTimeProvider(Start),
            KnownIssues.Empty, []);

        Result<JobOutcome> outcome = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 8, CancellationToken.None);

        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Equal(8, transport.Sent);
    }

    [Fact]
    public async Task FuzzJob_429ThenOk_RetriesOnceAndPasses()
    {
        int calls = 0;
        var transport = new FakeTransport(_ => calls++ == 0 ? Probe(429, retryAfter: "1") : Probe(200));
        var job = new FuzzJob(transport, new GatewayPacer(TimeProvider.System), TimeProvider.System, KnownIssues.Empty, []);

        Result<JobOutcome> outcome = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 1, CancellationToken.None);

        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Equal(2, transport.Sent);
        Assert.Equal(0, outcome.Value.Result!["throttled"]!.GetValue<int>());
    }

    [Fact]
    public async Task FuzzJob_Persistent429_IsThrottledNotFailure()
    {
        var transport = new FakeTransport(_ => Probe(429, retryAfter: "1"));
        var job = new FuzzJob(transport, new GatewayPacer(TimeProvider.System), TimeProvider.System, KnownIssues.Empty, []);

        Result<JobOutcome> outcome = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 1, CancellationToken.None);

        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Equal(1, outcome.Value.Result!["throttled"]!.GetValue<int>());
        Assert.Empty((JsonArray)outcome.Value.Result!["failures"]!);
    }

    [Fact]
    public async Task FuzzJob_CancelledBeforeStart_IsFailure()
    {
        var transport = new FakeTransport(_ => Probe(200));
        var job = new FuzzJob(transport, new GatewayPacer(new FakeTimeProvider(Start)), new FakeTimeProvider(Start), KnownIssues.Empty, []);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Result<JobOutcome> outcome = await job.RunAsync(Seed, FuzzTargets.Enroll, 0, 3, cancelled.Token);

        Assert.True(outcome.IsFailure);
        Assert.Contains("[Lab.Cancelled]", outcome.Error, StringComparison.Ordinal);
        Assert.Equal(0, transport.Sent);
    }

    private static HttpProbeResult Probe(int status, string? retryAfter = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "text/plain" };
        if (retryAfter is not null)
        {
            headers["Retry-After"] = retryAfter;
        }

        return new HttpProbeResult(status, headers, null, 12, "ok", null);
    }

    /// <summary>A fake transport: answers each case from a function, and counts the sends.</summary>
    private sealed class FakeTransport : IFuzzTransport
    {
        private readonly Func<FuzzCase, HttpProbeResult> _answer;

        public FakeTransport(Func<FuzzCase, HttpProbeResult> answer)
        {
            _answer = answer;
        }

        public int Sent { get; private set; }

        public Task<Result<HttpProbeResult>> SendAsync(FuzzTarget target, FuzzCase fuzzCase, CancellationToken ct)
        {
            Sent++;
            return Task.FromResult(Result<HttpProbeResult>.Success(_answer(fuzzCase)));
        }
    }
}
