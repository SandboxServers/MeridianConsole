using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// L25f: the ingest fills the <c>faults</c> and <c>pairs</c> tables from bundles that <see cref="BundleWriter"/> wrote. A twin pair
/// comes from the real writer (L53), never from a hand-written run.json.
/// </summary>
public sealed class FaultsAndPairsIngestTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private const string Spec = """
        schema = 1

        [section]
        id = "pairs-faults"
        title = "Pairs and faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-l25f-ingest-" + Guid.NewGuid().ToString("N"));
    private readonly string _uatRuns;
    private readonly SecretVault _vault = new();
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public FaultsAndPairsIngestTests()
    {
        _uatRuns = Path.Combine(_root, "uat-runs");
        _ = Directory.CreateDirectory(_uatRuns);
    }

    public void Dispose()
    {
        _vault.Dispose();
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
    public async Task Ingest_TwinRunFromTheWriter_GivesOnePairsRow()
    {
        string folder = WriteRun("run-l25f-pair", new PairInfo("exp-l25f", "base", string.Empty, "equal_outcome"), []);
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select experiment_id, role, partner_run_id, relation, held from pairs where run_id = 'run-l25f-pair';";
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("exp-l25f", reader.GetString(0));
        Assert.Equal("base", reader.GetString(1));
        Assert.Equal(string.Empty, reader.GetString(2));
        Assert.Equal("equal_outcome", reader.GetString(3));
        Assert.True(await reader.IsDBNullAsync(4));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Ingest_NoPair_WritesNoPairsRow()
    {
        string folder = WriteRun("run-l25f-solo", null, []);
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select count(*) from pairs where run_id = 'run-l25f-solo';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Ingest_FaultedRunFromTheWriter_GivesItsFaultsRows()
    {
        JsonElement args = JsonDocument.Parse("{\"target\":\"c:gateway\"}").RootElement.Clone();
        var kill = new UatRowFault("kill", "fault_kill", args, "f-l25f-1", Start, Start.AddSeconds(1), null, "kill", "c:gateway");
        var slow = new UatRowFault("slow", "fault_kill", args, "f-l25f-2", Start, null, "heal failed after timeout", "kill", "c:nodes");
        string folder = WriteRun("run-l25f-faults", null, [kill, slow]);
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select fault_id, kind, target_id, healed_at, heal_reason from faults where run_id = 'run-l25f-faults' order by fault_id;";
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("f-l25f-1", reader.GetString(0));
        Assert.Equal("kill", reader.GetString(1));
        Assert.Equal("c:gateway", reader.GetString(2));
        Assert.False(await reader.IsDBNullAsync(3));
        Assert.True(await reader.IsDBNullAsync(4));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("f-l25f-2", reader.GetString(0));
        Assert.Equal("c:nodes", reader.GetString(2));
        Assert.True(await reader.IsDBNullAsync(3));
        Assert.Equal("heal failed after timeout", reader.GetString(4));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Ingest_FaultWithoutKindOrTarget_IsSkipped_AndTheRunIsStillIngested()
    {
        JsonElement args = JsonDocument.Parse("{}").RootElement.Clone();
        var bare = new UatRowFault("bare", "fault_kill", args, "f-l25f-3", Start, Start.AddSeconds(1), null);
        string folder = WriteRun("run-l25f-bare", null, [bare]);
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;

        Result<IngestResult> result = await NewIngestor(db).IngestAsync(folder, [_uatRuns], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select count(*) from faults where run_id = 'run-l25f-bare';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        Assert.True(db.RunOf("run-l25f-bare").IsSuccess);
    }

    /// <summary>Writes one run with the real writer: one row <c>pairs-faults/faulted</c> carrying <paramref name="faults"/>.</summary>
    private string WriteRun(string runId, PairInfo? pair, IReadOnlyList<UatRowFault> faults)
    {
        var redactor = new Redactor(_vault);
        var writer = new BundleWriter(_uatRuns, _validator, redactor, new SecretScanner(_vault), new FakeTimeProvider(Start), "abcd1234",
            "linux", "build", null);
        SpecLoadOutcome loaded = SpecLoader.LoadText("pairs-faults.toml", Spec);
        Assert.NotNull(loaded.Spec);

        writer.RunStarted(runId, 7UL, loaded.Spec!);
        writer.RowFinished(new UatRowResult("faulted", RowGrade.Pass, string.Empty, [], StartedAt: Start, EndedAt: Start.AddSeconds(2),
            Faults: faults));
        writer.RunFinished(new UatRunResult(runId, 7UL, false, RunOverall.Pass, [], Pair: pair));

        Assert.Null(writer.WriteError);
        return writer.Folder!;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "results", ResultsDb.FileName)}");
        connection.Open();
        return connection;
    }

    private BundleIngestor NewIngestor(ResultsDb db) =>
        new(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [], NullLogger<BundleIngestor>.Instance);
}
