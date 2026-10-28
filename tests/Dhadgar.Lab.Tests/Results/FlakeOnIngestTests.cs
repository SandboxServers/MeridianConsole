using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// A flake is detected on ingest: the same section, row and seed graded differently by two runs of the same build. A grade
/// change across builds is a regression, not a flake.
/// </summary>
public sealed class FlakeOnIngestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-flakes-" + Path.GetRandomFileName());
    private readonly string _uatRuns;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public FlakeOnIngestTests()
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
    public async Task Ingest_SameBuildAndSeedGradedDifferently_RecordsOneFlake()
    {
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        BundleIngestor ingestor = new(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [], NullLogger<BundleIngestor>.Instance);
        string passed = BundleFixtures.Write(_uatRuns, "run-pass", "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
        string failed = BundleFixtures.Write(_uatRuns, "run-fail", "b1", 7, new RowSpec("nodes", "enroll", "FAIL", ClauseState: "NotHeld"));

        Assert.True((await ingestor.IngestAsync(passed, [_uatRuns], CancellationToken.None)).IsSuccess);
        Result<IngestResult> second = await ingestor.IngestAsync(failed, [_uatRuns], CancellationToken.None);

        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(1, db.CountFlakes());
    }

    [Fact]
    public async Task Ingest_SameBuildAndSeedSameGrade_RecordsNoFlake()
    {
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        BundleIngestor ingestor = new(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [], NullLogger<BundleIngestor>.Instance);
        string first = BundleFixtures.Write(_uatRuns, "run-1", "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
        string second = BundleFixtures.Write(_uatRuns, "run-2", "b1", 7, new RowSpec("nodes", "enroll", "PASS"));

        Assert.True((await ingestor.IngestAsync(first, [_uatRuns], CancellationToken.None)).IsSuccess);
        Assert.True((await ingestor.IngestAsync(second, [_uatRuns], CancellationToken.None)).IsSuccess);

        Assert.Equal(0, db.CountFlakes());
    }

    [Fact]
    public async Task Ingest_GradeChangeAcrossBuilds_IsNotAFlake()
    {
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        BundleIngestor ingestor = new(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [], NullLogger<BundleIngestor>.Instance);
        string older = BundleFixtures.Write(_uatRuns, "run-old", "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
        string newer = BundleFixtures.Write(_uatRuns, "run-new", "b2", 7, new RowSpec("nodes", "enroll", "FAIL", ClauseState: "NotHeld"));

        Assert.True((await ingestor.IngestAsync(older, [_uatRuns], CancellationToken.None)).IsSuccess);
        Assert.True((await ingestor.IngestAsync(newer, [_uatRuns], CancellationToken.None)).IsSuccess);

        Assert.Equal(0, db.CountFlakes());
    }
}
