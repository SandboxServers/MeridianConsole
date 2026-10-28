using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// Ingest is an upsert by run id (A2): one run after two ingests, signatures filled, and the second ingest replaces the grades.
/// Paths outside the allowed roots are refused, and an observer failure never fails the ingest (A3).
/// </summary>
public sealed class BundleIngestorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-ingest-" + Path.GetRandomFileName());
    private readonly string _uatRuns;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public BundleIngestorTests()
    {
        _uatRuns = Path.Combine(_root, "uat-runs");
        Directory.CreateDirectory(_uatRuns);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A WAL file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task Ingest_TwiceSameRun_KeepsOneRunAndItsRows()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-a", "b1", 42,
            new RowSpec("nodes", "enroll", "PASS"),
            new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        BundleIngestor ingestor = NewIngestor(db);

        Result<IngestResult> first = await ingestor.IngestAsync(folder, [_uatRuns], CancellationToken.None);
        Result<IngestResult> second = await ingestor.IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(1, db.CountRuns());
        Assert.Equal(2, db.CountRows("run-a"));
    }

    [Fact]
    public async Task Ingest_FailingRow_FillsSignaturesJson()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-b", "b1", 42,
            new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string? signatures = db.SignaturesJsonOf("run-b", "nodes", "heartbeat");
        Assert.NotNull(signatures);
        Assert.NotEqual("[]", signatures);
        Assert.Equal(1, db.CountSignatures());
        Assert.Equal(1, db.SignatureHits(SignatureBuilder.SignatureId(
            SignatureBuilder.Clause("nodes", "heartbeat", "c1", "NotHeld").Text)));
    }

    [Fact]
    public async Task Ingest_SecondIngestAfterRegrade_ReplacesTheGradeAndOverall()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-c", "b1", 42, new RowSpec("nodes", "enroll", "FAIL", ClauseState: "NotHeld"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        BundleIngestor ingestor = NewIngestor(db);
        Assert.True((await ingestor.IngestAsync(folder, [_uatRuns], CancellationToken.None)).IsSuccess);
        Assert.Equal("fail", db.OverallOf("run-c"));

        // The attested row is written back as PASS (uat_attest re-ingests, A2).
        BundleFixtures.Write(_uatRuns, "run-c", "b1", 42, new RowSpec("nodes", "enroll", "PASS"));
        Result<IngestResult> second = await ingestor.IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal("pass", db.OverallOf("run-c"));
        Assert.Equal(1, db.CountRows("run-c"));
    }

    [Fact]
    public async Task Ingest_FolderOutsideTheAllowedRoots_IsRefused()
    {
        string outside = BundleFixtures.Write(Path.Combine(_root, "elsewhere"), "run-d", "b1", 42,
            new RowSpec("nodes", "enroll", "PASS"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(outside, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, db.CountRuns());
    }

    [Fact]
    public async Task Ingest_ObserverThrows_IngestStillSucceedsAndLaterObserversRun()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-e", "b1", 42, new RowSpec("nodes", "enroll", "PASS"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        var later = new RecordingObserver();
        BundleIngestor ingestor = new(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text,
            [new ThrowingObserver(), later], NullLogger<BundleIngestor>.Instance);

        Result<IngestResult> result = await ingestor.IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, db.CountRuns());
        Assert.Equal(["run-e"], later.Seen);
    }

    private static BundleIngestor NewIngestor(ResultsDb db) =>
        new(db, new PathValidator(NullLogger<PathValidator>.Instance), KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [],
            NullLogger<BundleIngestor>.Instance);

    private sealed class ThrowingObserver : IIngestObserver
    {
        public Task OnIngestedAsync(string runId, CancellationToken ct) => throw new InvalidOperationException("observer");
    }

    private sealed class RecordingObserver : IIngestObserver
    {
        public List<string> Seen { get; } = [];

        public Task OnIngestedAsync(string runId, CancellationToken ct)
        {
            Seen.Add(runId);
            return Task.CompletedTask;
        }
    }
}
