using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The results service (D-ML91, D-ML92): unchecked known-issue ingests say so, checked ones record the file hash and the
/// issue count, and an observer that reads the index during an ingest does not deadlock on the gate.
/// </summary>
public sealed class ResultsServiceTests : IDisposable
{
    private const string OneIssueFile = """
        schema = 1

        [[issue]]
        id = "KI-TEST-ONE"
        title = "Test issue for the checked ingest"
        link = "docs/PROJECT-STATE.md"
        signatures = ["inv:I1|nodes|unhandled"]
        patterns = []
        state = "open"

        [issue.scope]
        sections = ["invariants"]
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-results-svc-" + Path.GetRandomFileName());
    private readonly string _uatRuns;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));

    public ResultsServiceTests()
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
    public async Task Ingest_UncheckedKnownIssues_RecordsUnchecked()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-unchecked", "b1", 42,
            new RowSpec("nodes", "heartbeat", "FAIL", ClauseState: "NotHeld"));
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using ResultsService results = NewService(LabPaths.FromRoot(_root, repoRoot: ""), redactor);

        Result<IngestResult> ingested = await results.IngestAsync(folder, CancellationToken.None);

        Assert.True(ingested.IsSuccess, ingested.Error);
        JsonObject payload = results.ToPayload(ingested);
        Assert.True(payload["ok"]!.GetValue<bool>());
        Assert.False(payload["known_issues"]!["checked"]!.GetValue<bool>());
        Assert.Equal("Lab:RepoRoot is not set", payload["known_issues"]!["reason"]!.GetValue<string>());

        Result<RunRecord> run = await OpenRead(results, db => db.RunOf("run-unchecked"));
        Assert.True(run.IsSuccess, run.Error);
        Assert.Equal("{\"checked\":false,\"reason\":\"Lab:RepoRoot is not set\"}", run.Value.KnownIssuesJson);
        Assert.Null(RowKnownMatch(Path.Combine(_root, "results", "lab-results.db"), "run-unchecked"));
    }

    [Fact]
    public async Task Ingest_CheckedKnownIssues_RecordsShaAndCount()
    {
        string repoRoot = Path.Combine(_root, "repo");
        string specs = Path.Combine(repoRoot, "docs", "guides", "uat-specs");
        Directory.CreateDirectory(specs);
        string file = Path.Combine(specs, "known-issues.toml");
        await File.WriteAllTextAsync(file, OneIssueFile, CancellationToken.None);
        string expectedSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, CancellationToken.None)))
            .ToLowerInvariant();
        string folder = BundleFixtures.Write(_uatRuns, "run-checked", "b1", 42,
            new RowSpec("invariants", "I1", "FAIL", Violation: "I1", CheckId: "unhandled"));
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using ResultsService results = NewService(LabPaths.FromRoot(_root, repoRoot), redactor);

        Result<IngestResult> ingested = await results.IngestAsync(folder, CancellationToken.None);

        Assert.True(ingested.IsSuccess, ingested.Error);
        JsonObject known = results.ToPayload(ingested)["known_issues"]!.AsObject();
        Assert.True(known["checked"]!.GetValue<bool>());
        Assert.Equal(1, known["issues"]!.GetValue<int>());
        Assert.Equal(expectedSha, known["file_sha256"]!.GetValue<string>());

        Result<RunRecord> run = await OpenRead(results, db => db.RunOf("run-checked"));
        Assert.True(run.IsSuccess, run.Error);
        JsonNode? stored = JsonNode.Parse(run.Value.KnownIssuesJson!);
        Assert.True(stored!["checked"]!.GetValue<bool>());
        Assert.Equal(expectedSha, stored["file_sha256"]!.GetValue<string>());
        Assert.Equal("KI-TEST-ONE", RowKnownMatch(Path.Combine(_root, "results", "lab-results.db"), "run-checked"));
    }

    [Fact]
    public async Task Ingest_ObserverCallsBack_NoDeadlock()
    {
        string folder = BundleFixtures.Write(_uatRuns, "run-observer", "b1", 42, new RowSpec("nodes", "enroll", "PASS"));
        var observer = new ReadingObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IIngestObserver>(observer);
        using ServiceProvider provider = services.BuildServiceProvider();
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using ResultsService results = new(LabPaths.FromRoot(_root, repoRoot: ""), _validator, redactor, provider,
            NullLoggerFactory.Instance, _time);
        observer.Results = results;

        Task<Result<IngestResult>> ingest = results.IngestAsync(folder, CancellationToken.None);
        Task finished = await Task.WhenAny(ingest, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(ingest, finished);
        Assert.True((await ingest).IsSuccess);
        Assert.True(observer.ReadSucceeded);
    }

    private ResultsService NewService(LabPaths paths, Redactor redactor) =>
        new(paths, _validator, redactor, new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance, _time);

    private static Task<Result<T>> OpenRead<T>(ResultsService results, Func<ResultsDb, Result<T>> read) =>
        results.ReadAsync(read, CancellationToken.None);

    /// <summary>Reads the <c>known_match</c> column of the one row of a run straight from the database file.</summary>
    private static string? RowKnownMatch(string databasePath, string runId)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select known_match from rows where run_id = $run_id limit 1;";
        command.Parameters.AddWithValue("$run_id", runId);
        return command.ExecuteScalar() as string;
    }

    private sealed class ReadingObserver : IIngestObserver
    {
        public ResultsService? Results { get; set; }

        public bool ReadSucceeded { get; private set; }

        public async Task OnIngestedAsync(string runId, CancellationToken ct)
        {
            Result<long> count = await Results!.ReadAsync(db => Result<long>.Success(db.CountRuns()), ct);
            ReadSucceeded = count.IsSuccess;
        }
    }
}
