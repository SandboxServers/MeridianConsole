namespace Dhadgar.Lab.Results;

/// <summary>
/// A seam that runs after each successful ingest (A3). <see cref="BundleIngestor"/> calls every registered observer in
/// registration order. An observer failure is logged redacted and never fails the ingest. L25 registers none; L26 and L52
/// register theirs in their own area files.
/// </summary>
public interface IIngestObserver
{
    /// <summary>Called after the run <paramref name="runId"/> has been written to the index.</summary>
    /// <param name="runId">The ingested run id.</param>
    /// <param name="ct">The cancellation token of the ingest.</param>
    Task OnIngestedAsync(string runId, CancellationToken ct);
}
