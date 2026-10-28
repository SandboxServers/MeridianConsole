using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The results database opens in WAL mode, migrates forward only and takes every value as a parameter.
/// </summary>
public sealed class ResultsDbTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-results-" + Path.GetRandomFileName());
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

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
    public void Open_EnablesWalMode()
    {
        Result<ResultsDb> opened = ResultsDb.Open(_root, _validator);

        Assert.True(opened.IsSuccess, opened.Error);
        using ResultsDb db = opened.Value;
        Assert.Equal("wal", db.JournalMode());
    }

    [Fact]
    public void Migrate_OpenedTwice_KeepsOneSchemaVersion()
    {
        using (ResultsDb first = ResultsDb.Open(_root, _validator).Value)
        {
            Assert.Equal(ResultsDb.LatestSchemaVersion, first.SchemaVersion);
        }

        using ResultsDb second = ResultsDb.Open(_root, _validator).Value;
        Assert.Equal(ResultsDb.LatestSchemaVersion, second.SchemaVersion);
        Assert.Equal(0, second.CountRuns());
    }

    [Fact]
    public void UpsertRun_SameRunTwice_KeepsOneRun()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;
        RunRecord run = NewRun("run-1");

        Assert.True(db.UpsertRun(run).IsSuccess);
        Assert.True(db.UpsertRun(run with { Overall = "fail" }).IsSuccess);

        Assert.Equal(1, db.CountRuns());
        Assert.Equal("fail", db.OverallOf("run-1"));
    }

    [Fact]
    public void Insert_HostileRunId_IsStoredAsData()
    {
        const string hostile = "x'); drop table runs; --";
        using (ResultsDb db = ResultsDb.Open(_root, _validator).Value)
        {
            Assert.True(db.UpsertRun(NewRun(hostile)).IsSuccess);
        }

        using ResultsDb reopened = ResultsDb.Open(_root, _validator).Value;
        Assert.Equal(1, reopened.CountRuns());
        Assert.Equal(ResultsDb.LatestSchemaVersion, reopened.SchemaVersion);
    }

    private static RunRecord NewRun(string runId) =>
        new(runId, "uat", "label", 42, "build1", "abc123", false, "2026-10-28T00:00:00Z", "2026-10-28T00:01:00Z",
            "pass", null, "evidence_only", "C:/bundle");
}
