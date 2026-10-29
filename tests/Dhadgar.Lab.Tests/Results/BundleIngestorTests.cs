using System.Text;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// Ingest is an upsert by run id (A2): one run after two ingests, signatures filled, and the second ingest replaces the grades.
/// Paths outside the allowed roots are refused, and an observer failure never fails the ingest (A3).
/// </summary>
public sealed class BundleIngestorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 0, 0, 30, TimeSpan.Zero);

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

    [Fact]
    public async Task Ingest_SignatureKinds_AreOnlyBuiltKinds()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-kinds", "b1", 42,
            new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld", Violation: "I1"),
            new RowSpec("nodes", "enroll", "FAIL", Violation: "I1"));
        string resultsRoot = Path.Combine(_root, "results");
        using ResultsDb db = ResultsDb.Open(resultsRoot, _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        List<string> kinds = SignatureKindsOf(Path.Combine(resultsRoot, "lab-results.db"));
        Assert.NotEmpty(kinds);
        Assert.All(kinds, kind => Assert.Contains(kind, EvidenceCoverage.BuiltKinds));
    }

    [Fact]
    public async Task Ingest_RunEndViolationRow_IsInvSignature()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-end", "b1", 42,
            new RowSpec("invariants", "I1", "FAIL", Violation: "I1", CheckId: "unhandled"));
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("fail", result.Value.Overall);
        string text = SignatureBuilder.Invariant("I1", "nodes", "unhandled").Text;
        Assert.Equal("inv:I1|nodes|unhandled", text);
        Assert.Equal(1, db.SignatureHits(SignatureBuilder.SignatureId(text)));
        Assert.Contains("invariants/I1", RunSummary.Compute([new GradedRow("invariants", "I1", RowGrade.Fail)],
            new Dictionary<string, KnownMatch>(StringComparer.Ordinal)).NewFailures);
    }

    [Fact]
    public async Task Ingest_HistoryBundle_IsRefused()
    {
        string folder = Path.Combine(_uatRuns, "20261028-hist-run-h");
        Directory.CreateDirectory(folder);
        var run = new JsonObject { ["schema"] = 1, ["run_id"] = "run-h", ["kind"] = "history", ["seed"] = 42 };
        await File.WriteAllTextAsync(Path.Combine(folder, "run.json"), run.ToJsonString(), CancellationToken.None);
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.InvalidArgument] history bundles are not ingested until L25f", result.Error);
        Assert.True(db.RunOf("run-h").IsFailure);
        Assert.Contains("[Lab.NotFound]", db.RunOf("run-h").Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingest_PlantedFiveHundredWithException_GivesOneExcAndOneHttpSignature_AndNoSecretInTheIndex()
    {
        const string Secret = "ingest-secret-4412";
        using var vault = new SecretVault();
        _ = vault.Add(SecretKind.Password, Secret);
        var redactor = new Redactor(vault);
        var writer = new BundleWriter(_uatRuns, _validator, redactor, new SecretScanner(vault), new FakeTimeProvider(Start), "abcd1234", "linux", "build", null);
        writer.UseLogs(new StaticLogSource(new RunLogSlice("nodes", [
            new LogLine(Start, "Error", "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", null,
                "System.InvalidOperationException: enroll failed with " + Secret + "\n"
                + "   at Dhadgar.Nodes.Enroll.HandleAsync(Object o) in /src/Enroll.cs:line 42"),
            new LogLine(Start, "Information", "Microsoft.AspNetCore.Hosting.Diagnostics", null,
                "Request finished HTTP/1.1 POST http://nodes:5000/api/v1/agents/enroll?t=" + Secret + " - 500 - text/plain 10.0ms"),
        ])));
        SpecLoadOutcome loaded = SpecLoader.LoadText("nodes-enroll.toml", IngestSpec);
        Assert.NotNull(loaded.Spec);
        writer.RunStarted("run-exc", 42UL, loaded.Spec!);
        writer.RowFinished(new UatRowResult("enroll", RowGrade.Fail, "500 on enroll", [],
            StartedAt: Start.AddSeconds(-1), EndedAt: Start));
        writer.RunFinished(new UatRunResult("run-exc", 42UL, false, RunOverall.Incomplete, []));
        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);

        string resultsRoot = Path.Combine(_root, "results");
        using (ResultsDb db = ResultsDb.Open(resultsRoot, _validator).Value)
        {
            Result<IngestResult> result = await NewIngestor(db).IngestAsync(writer.Folder!, [_uatRuns], CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(2, db.CountSignatures());
        }

        List<string> kinds = SignatureKindsOf(Path.Combine(resultsRoot, "lab-results.db"));
        Assert.Equal(["exc", "http"], kinds.Order().ToArray());
        SqliteConnection.ClearAllPools();
        byte[] needle = Encoding.UTF8.GetBytes(Secret);
        foreach (string file in Directory.EnumerateFiles(resultsRoot))
        {
            byte[] bytes = await File.ReadAllBytesAsync(file, CancellationToken.None);
            Assert.True(bytes.AsSpan().IndexOf(needle) < 0, Path.GetFileName(file));
        }
    }

    private const string IngestSpec = """
        schema = 1

        [section]
        id = "nodes"
        title = "Nodes"

        [[row]]
        id = "enroll"
        title = "Enroll"
        expected = "pass"
        depends_on = []
        """;

    private static List<string> SignatureKindsOf(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select kind from signatures;";
        using SqliteDataReader reader = command.ExecuteReader();
        var kinds = new List<string>();
        while (reader.Read())
        {
            kinds.Add(reader.GetString(0));
        }

        return kinds;
    }

    private static BundleIngestor NewIngestor(ResultsDb db) =>
        new(db, new PathValidator(NullLogger<PathValidator>.Instance), KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [],
            NullLogger<BundleIngestor>.Instance);

    private sealed class StaticLogSource(params RunLogSlice[] slices) : IRunLogSource
    {
        public IReadOnlyList<RunLogSlice> Slices(DateTimeOffset from, DateTimeOffset to) => slices;
    }

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
