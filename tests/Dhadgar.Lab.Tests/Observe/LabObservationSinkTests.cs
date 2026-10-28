using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// The observation sink assigns a total order, redacts, caps and closes open ops.
/// </summary>
public sealed class LabObservationSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "l48-sink-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault _vault = new();
    private readonly LabObservationSink _sink;

    public LabObservationSinkTests()
    {
        _sink = new LabObservationSink(new Redactor(_vault), TimeProvider.System, new PathValidator(NullLogger<PathValidator>.Instance), [_root]);
    }

    public void Dispose()
    {
        _sink.Dispose();
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Sink_ParallelAppends_SeqTotalOrder()
    {
        const string runId = "run-parallel";
        string folder = Path.Combine(_root, runId);
        Assert.True(_sink.BeginRun(runId, folder).IsSuccess);

        var seqs = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = Enumerable.Range(0, 8).Select(actor => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                Result<long> seq = _sink.Append(Draft(runId, ActorIds.Client(actor), ObservationKind.Observe, "tick", seq: 0));
                Assert.True(seq.IsSuccess);
                seqs.Add(seq.Value);
            }
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(1, 8000).Select(n => (long)n), seqs.Order());
        Assert.True((await _sink.EndRunAsync(runId, CancellationToken.None)).IsSuccess);

        long[] fileSeqs = (await File.ReadAllLinesAsync(Path.Combine(folder, LabObservationSink.FileName)))
            .Select(line => JsonSerializer.Deserialize<LabObservation>(line, ObserveJson.Options)!.Seq)
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 8000).Select(n => (long)n), fileSeqs);
    }

    [Fact]
    public void Sink_VaultValueInArgs_Redacted()
    {
        const string runId = "run-redact";
        string folder = Path.Combine(_root, runId);
        Assert.True(_sink.BeginRun(runId, folder).IsSuccess);
        string secret = "s3cret-value-123";
        _vault.Add(SecretKind.EnrollmentToken, secret);
        using JsonDocument args = JsonDocument.Parse("{\"token\":\"" + secret + "\"}");

        Assert.True(_sink.Append(Draft(runId, ActorIds.Runner, ObservationKind.Invoke, "enroll",
            opId: runId + "/runner/1", args: args.RootElement.Clone())).IsSuccess);

        string text;
        using (var stream = new FileStream(Path.Combine(folder, LabObservationSink.FileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }
        Assert.DoesNotContain(secret, text);
        Assert.Contains("***", text);
    }

    [Fact]
    public async Task Sink_OverCap_TruncatedNoteOnce()
    {
        const string runId = "run-cap";
        string folder = Path.Combine(_root, runId);
        Assert.True(_sink.BeginRun(runId, folder).IsSuccess);

        for (int i = 0; i < LabObservationSink.MaxObservationsPerRun; i++)
        {
            Assert.True(_sink.Append(Draft(runId, ActorIds.Lab, ObservationKind.Observe, "tick", seq: 0)).IsSuccess);
        }

        Assert.True(_sink.Append(Draft(runId, ActorIds.Lab, ObservationKind.Observe, "tick", seq: 0)).IsFailure);
        Assert.True(_sink.Append(Draft(runId, ActorIds.Lab, ObservationKind.Observe, "tick", seq: 0)).IsFailure);
        Assert.True((await _sink.EndRunAsync(runId, CancellationToken.None)).IsSuccess);

        string[] lines = await File.ReadAllLinesAsync(Path.Combine(folder, LabObservationSink.FileName));
        Assert.Equal(LabObservationSink.MaxObservationsPerRun + 1, lines.Length);
        Assert.Single(lines, line => line.Contains("\"op\":\"truncated\""));
    }

    [Fact]
    public async Task EndRun_OpenOp_ClosedUnknown()
    {
        const string runId = "run-open";
        string folder = Path.Combine(_root, runId);
        Assert.True(_sink.BeginRun(runId, folder).IsSuccess);
        OpId op = _sink.NextOp(runId, ActorIds.Driver);
        Assert.True(_sink.Append(Draft(runId, ActorIds.Driver, ObservationKind.Invoke, "http_request", opId: op.ToString())).IsSuccess);

        Result<long> count = await _sink.EndRunAsync(runId, CancellationToken.None);

        Assert.Equal(2, count.Value);
        IReadOnlyList<LabObservation> readBack = _sink.Read(runId, 0, 10);
        LabObservation last = readBack[readBack.Count - 1];
        Assert.Equal(ObservationKind.Complete, last.Kind);
        Assert.Equal(OpOutcome.Unknown, last.Outcome);
        Assert.Equal(op.ToString(), last.OpId);
        Assert.Equal("run ended", last.Result.GetProperty("reason").GetString());
    }

    [Fact]
    public void Append_NoOpenRun_Fails()
    {
        Result<long> result = _sink.Append(Draft("run-never-begun", ActorIds.Lab, ObservationKind.Note, "note", seq: 0));

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.NotFound]", result.Error);
    }

    private static LabObservation Draft(
        string runId, string actor, ObservationKind kind, string op, long seq = 0, string? opId = null, JsonElement? args = null)
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        return new LabObservation(
            seq, runId, DateTimeOffset.UnixEpoch, actor, opId, kind, op,
            args ?? empty.RootElement.Clone(), null, empty.RootElement.Clone(), null);
    }
}
