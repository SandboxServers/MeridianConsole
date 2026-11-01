using Dhadgar.Lab.Observe;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// An in-memory observation sink for the history client: it keeps every appended line in order, so tests can read the
/// invoke and complete lines back. Shared by the unit tests and the S-L2h seam test.
/// </summary>
internal sealed class FakeObservationSink : ILabObservationSink
{
    private long _seq;

    /// <summary>Every appended line, in append order, with its sequence number set.</summary>
    public List<LabObservation> Lines { get; } = [];

    /// <inheritdoc />
    public Result BeginRun(string runId, string runFolder) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => throw new NotSupportedException();

    /// <inheritdoc />
    public OpId NextOp(string runId, string actor) => new(runId, actor, ++_seq);

    /// <inheritdoc />
    public Result<long> Append(LabObservation draft)
    {
        Lines.Add(draft with { Seq = Lines.Count + 1 });
        return Result<long>.Success(Lines.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => throw new NotSupportedException();
}
