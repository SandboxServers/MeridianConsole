using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;
using Microsoft.Data.Sqlite;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The lab results database (C12): an index over the evidence bundles at <c>LabPaths.Results\lab-results.db</c>. The bundles
/// stay the evidence; this file is rebuilt from them by ingest. It opens in WAL mode and migrates forward only. Every value
/// reaches SQL as a parameter.
/// </summary>
public sealed class ResultsDb : IDisposable
{
    /// <summary>The file name under <c>LabPaths.Results</c>.</summary>
    public const string FileName = "lab-results.db";

    /// <summary>
    /// Forward-only migrations. Migration <c>n</c> is element <c>n - 1</c>; a database at version <c>v</c> runs the rest.
    /// </summary>
    private static readonly string[] Migrations =
    [
        """
        create table if not exists runs (run_id text primary key, kind text not null, label text, seed integer not null,
          build_id text, commit_sha text, dirty integer, started_at text, ended_at text, overall text,
          fingerprint_sha256 text, fidelity text, bundle_path text not null);
        create table if not exists rows (run_id text not null, section text not null, row_id text not null, grade text not null,
          reason text, seed integer not null, known_issue text, signature_id text,
          signatures_json text not null default '[]', known_match text, started_at text, ended_at text,
          primary key (run_id, section, row_id));
        create table if not exists pairs (experiment_id text not null, run_id text not null, role text not null,
          partner_run_id text not null, relation text not null, held integer, primary key (experiment_id, run_id));
        create table if not exists coverage (run_id text not null, kind text not null, key text not null, hits integer not null,
          primary key (run_id, kind, key));
        create table if not exists faults (run_id text not null, fault_id text not null, kind text not null, target_id text not null,
          args_json text, started_at text, healed_at text, heal_reason text, primary key (run_id, fault_id));
        create table if not exists invariant_violations (run_id text not null, invariant_id text not null, service text not null,
          check_id text not null, signature_id text not null, at text not null, allowed integer not null, section text,
          row_id text, detail text);
        create table if not exists signatures (signature_id text primary key, kind text not null, text text not null,
          first_seen text not null, last_seen text not null, hits integer not null, state text not null,
          known_issue_id text, issue_url text);
        create table if not exists findings (finding_id text primary key, signature_id text not null, first_run_id text not null,
          repro_path text, draft_path text, state text not null, created_at text not null);
        create table if not exists flakes (section text not null, row_id text not null, seed integer not null,
          run_ids_json text not null, grades_json text not null, detected_at text not null);
        """,
    ];

    private static readonly object InitLock = new();
    private static bool _nativeReady;

    private readonly SqliteConnection _connection;
    private bool _disposed;

    private ResultsDb(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>The schema version this build migrates to.</summary>
    public static int LatestSchemaVersion => Migrations.Length;

    /// <summary>
    /// Opens (creating when absent) the results database under <paramref name="resultsRoot"/>, switches it to WAL and runs
    /// the pending migrations. Returns a failure, never throws, for path, IO or SQLite errors.
    /// </summary>
    /// <param name="resultsRoot">The <c>LabPaths.Results</c> folder.</param>
    /// <param name="validator">Validates the database path against the results root.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2000",
        Justification = "The connection is owned by the ResultsDb returned to the caller, which disposes it.")]
    public static Result<ResultsDb> Open(string resultsRoot, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (string.IsNullOrWhiteSpace(resultsRoot))
        {
            return Result<ResultsDb>.Failure("[Lab.InvalidArgument] the results root is required");
        }

        Result<string> path = validator.ValidatePath(Path.Combine(resultsRoot, FileName), [resultsRoot]);
        if (path.IsFailure)
        {
            return Result<ResultsDb>.Failure("[Lab.InvalidArgument] the results database path is outside the results root");
        }

        try
        {
            Directory.CreateDirectory(resultsRoot);
            EnsureNativeLoaded();
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path.Value,
                Mode = SqliteOpenMode.ReadWriteCreate,
            };
            var connection = new SqliteConnection(builder.ToString());
            try
            {
                connection.Open();
                using (SqliteCommand wal = connection.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode=WAL;";
                    wal.ExecuteScalar();
                }

                Migrate(connection);
                return Result<ResultsDb>.Success(new ResultsDb(connection));
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException)
        {
            return Result<ResultsDb>.Failure("[Lab.IoError] the results database could not be opened: " + ex.GetType().Name);
        }
    }

    /// <summary>The schema version stored in the database.</summary>
    public int SchemaVersion => ReadVersion(_connection);

    /// <summary>The journal mode the database reports (<c>wal</c> after <see cref="Open"/>).</summary>
    public string JournalMode()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Inserts a run, or replaces its summary columns when the run id exists (ingest is an upsert, A2).</summary>
    public Result UpsertRun(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Execute(
            """
            insert into runs (run_id, kind, label, seed, build_id, commit_sha, dirty, started_at, ended_at, overall,
              fingerprint_sha256, fidelity, bundle_path)
            values ($run_id, $kind, $label, $seed, $build_id, $commit_sha, $dirty, $started_at, $ended_at, $overall,
              $fingerprint, $fidelity, $bundle_path)
            on conflict(run_id) do update set kind = excluded.kind, label = excluded.label, seed = excluded.seed,
              build_id = excluded.build_id, commit_sha = excluded.commit_sha, dirty = excluded.dirty,
              started_at = excluded.started_at, ended_at = excluded.ended_at, overall = excluded.overall,
              fingerprint_sha256 = excluded.fingerprint_sha256, fidelity = excluded.fidelity,
              bundle_path = excluded.bundle_path;
            """,
            ("$run_id", run.RunId), ("$kind", run.Kind), ("$label", run.Label), ("$seed", run.Seed),
            ("$build_id", run.BuildId), ("$commit_sha", run.CommitSha), ("$dirty", Flag(run.Dirty)),
            ("$started_at", run.StartedAt), ("$ended_at", run.EndedAt), ("$overall", run.Overall),
            ("$fingerprint", run.FingerprintSha256), ("$fidelity", run.Fidelity), ("$bundle_path", run.BundlePath));
    }

    /// <summary>Removes a run's rows, pairs, faults and violations so a re-ingest can write them again.</summary>
    public Result DeleteRunDetails(string runId)
    {
        return Execute(
            """
            delete from rows where run_id = $run_id;
            delete from pairs where run_id = $run_id;
            delete from faults where run_id = $run_id;
            delete from invariant_violations where run_id = $run_id;
            """,
            ("$run_id", runId));
    }

    /// <summary>Inserts one graded row of a run.</summary>
    public Result InsertRow(RowRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Execute(
            """
            insert into rows (run_id, section, row_id, grade, reason, seed, known_issue, signature_id, signatures_json,
              known_match, started_at, ended_at)
            values ($run_id, $section, $row_id, $grade, $reason, $seed, $known_issue, $signature_id, $signatures_json,
              $known_match, $started_at, $ended_at);
            """,
            ("$run_id", row.RunId), ("$section", row.Section), ("$row_id", row.RowId), ("$grade", row.Grade),
            ("$reason", row.Reason), ("$seed", row.Seed), ("$known_issue", row.KnownIssue), ("$signature_id", row.SignatureId),
            ("$signatures_json", row.SignaturesJson), ("$known_match", row.KnownMatch),
            ("$started_at", row.StartedAt), ("$ended_at", row.EndedAt));
    }

    /// <summary>Inserts one fault of a run.</summary>
    public Result InsertFault(FaultRecord fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return Execute(
            """
            insert into faults (run_id, fault_id, kind, target_id, args_json, started_at, healed_at, heal_reason)
            values ($run_id, $fault_id, $kind, $target_id, $args_json, $started_at, $healed_at, $heal_reason);
            """,
            ("$run_id", fault.RunId), ("$fault_id", fault.FaultId), ("$kind", fault.Kind), ("$target_id", fault.TargetId),
            ("$args_json", fault.ArgsJson), ("$started_at", fault.StartedAt), ("$healed_at", fault.HealedAt),
            ("$heal_reason", fault.HealReason));
    }

    /// <summary>Inserts one invariant violation of a run.</summary>
    public Result InsertViolation(ViolationRecord violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        return Execute(
            """
            insert into invariant_violations (run_id, invariant_id, service, check_id, signature_id, at, allowed, section,
              row_id, detail)
            values ($run_id, $invariant_id, $service, $check_id, $signature_id, $at, $allowed, $section, $row_id, $detail);
            """,
            ("$run_id", violation.RunId), ("$invariant_id", violation.InvariantId), ("$service", violation.Service),
            ("$check_id", violation.CheckId), ("$signature_id", violation.SignatureId), ("$at", violation.At),
            ("$allowed", violation.Allowed ? 1L : 0L), ("$section", violation.Section), ("$row_id", violation.RowId),
            ("$detail", violation.Detail));
    }

    /// <summary>Inserts one experiment pair of a run (C18 pairs).</summary>
    public Result InsertPair(PairRecord pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        return Execute(
            """
            insert into pairs (experiment_id, run_id, role, partner_run_id, relation, held)
            values ($experiment_id, $run_id, $role, $partner_run_id, $relation, $held);
            """,
            ("$experiment_id", pair.ExperimentId), ("$run_id", pair.RunId), ("$role", pair.Role),
            ("$partner_run_id", pair.PartnerRunId), ("$relation", pair.Relation),
            ("$held", Flag(pair.Held)));
    }

    /// <summary>
    /// Inserts a signature, or on conflict widens its first and last seen and keeps its state and issue link unless new
    /// values are given. Hits are recounted by <see cref="RecountSignatureHits"/>.
    /// </summary>
    public Result UpsertSignature(SignatureRecord signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return Execute(
            """
            insert into signatures (signature_id, kind, text, first_seen, last_seen, hits, state, known_issue_id, issue_url)
            values ($id, $kind, $text, $seen, $seen, 0, $state, $known_issue_id, null)
            on conflict(signature_id) do update set first_seen = min(first_seen, excluded.first_seen),
              last_seen = max(last_seen, excluded.last_seen),
              state = coalesce($state, state),
              known_issue_id = coalesce($known_issue_id, known_issue_id);
            """,
            ("$id", signature.SignatureId), ("$kind", signature.Kind), ("$text", signature.Text), ("$seen", signature.SeenAt),
            ("$state", signature.State), ("$known_issue_id", signature.KnownIssueId));
    }

    /// <summary>Sets a signature's hit count to the number of graded rows whose signatures include it.</summary>
    public Result RecountSignatureHits(string signatureId)
    {
        return Execute(
            """
            update signatures set hits = (select count(*) from rows where rows.signatures_json like $pattern escape '\')
            where signature_id = $id;
            """,
            ("$id", signatureId), ("$pattern", "%\"" + signatureId + "\"%"));
    }

    /// <summary>Maps a nullable flag to the 0/1 integer SQLite stores.</summary>
    private static long? Flag(bool? value) => value is null ? null : value.Value ? 1L : 0L;

    /// <summary>Replaces the flake records for one row and seed.</summary>
    public Result ReplaceFlake(FlakeRecord flake)
    {
        ArgumentNullException.ThrowIfNull(flake);
        return Execute(
            """
            delete from flakes where section = $section and row_id = $row_id and seed = $seed;
            insert into flakes (section, row_id, seed, run_ids_json, grades_json, detected_at)
            values ($section, $row_id, $seed, $run_ids, $grades, $detected);
            """,
            ("$section", flake.Section), ("$row_id", flake.RowId), ("$seed", flake.Seed),
            ("$run_ids", flake.RunIdsJson), ("$grades", flake.GradesJson), ("$detected", flake.DetectedAt));
    }

    /// <summary>Counts the runs in the index.</summary>
    public long CountRuns() => ScalarLong("select count(*) from runs;");

    /// <summary>Counts the rows of one run.</summary>
    public long CountRows(string runId) => ScalarLong("select count(*) from rows where run_id = $run_id;", ("$run_id", runId));

    /// <summary>Counts the signatures in the index.</summary>
    public long CountSignatures() => ScalarLong("select count(*) from signatures;");

    /// <summary>Counts the flake records in the index.</summary>
    public long CountFlakes() => ScalarLong("select count(*) from flakes;");

    /// <summary>The stored <c>overall</c> of a run, or null when the run is absent or has none.</summary>
    public string? OverallOf(string runId) =>
        ScalarText("select overall from runs where run_id = $run_id;", ("$run_id", runId));

    /// <summary>The stored hit count of a signature, or -1 when absent.</summary>
    public long SignatureHits(string signatureId) =>
        ScalarLongOr("select hits from signatures where signature_id = $id;", -1, ("$id", signatureId));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The SQL is a literal in this class; every value is bound as a parameter.")]
    private Result Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
            return Result.Success();
        }
        catch (SqliteException ex)
        {
            return Result.Failure("[Lab.IoError] results database write failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    private long ScalarLong(string sql, params (string Name, object? Value)[] parameters) =>
        ScalarLongOr(sql, 0, parameters);

    private long ScalarLongOr(string sql, long fallback, params (string Name, object? Value)[] parameters)
    {
        object? value = Scalar(sql, parameters);
        return value is null or DBNull ? fallback : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? ScalarText(string sql, params (string Name, object? Value)[] parameters)
    {
        object? value = Scalar(sql, parameters);
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The SQL is a literal in this class; every value is bound as a parameter.")]
    private object? Scalar(string sql, (string Name, object? Value)[] parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteScalar();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Migrations are literals in this class; the version is bound as a parameter.")]
    private static void Migrate(SqliteConnection connection)
    {
        using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = "create table if not exists schema_version (version integer not null);";
            create.ExecuteNonQuery();
        }

        int current = ReadVersion(connection);
        for (int version = current + 1; version <= Migrations.Length; version++)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = Migrations[version - 1];
                migrate.ExecuteNonQuery();
            }

            using (SqliteCommand stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText = "delete from schema_version;";
                stamp.ExecuteNonQuery();
            }

            using (SqliteCommand record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "insert into schema_version (version) values ($version);";
                record.Parameters.AddWithValue("$version", version);
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select max(version) from schema_version;";
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void EnsureNativeLoaded()
    {
        lock (InitLock)
        {
            if (!_nativeReady)
            {
                SQLitePCL.Batteries_V2.Init();
                _nativeReady = true;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }
}

/// <summary>A run row of the index (C12 <c>runs</c>).</summary>
public sealed record RunRecord(string RunId, string Kind, string? Label, long Seed, string? BuildId, string? CommitSha,
    bool? Dirty, string? StartedAt, string? EndedAt, string? Overall, string? FingerprintSha256, string? Fidelity,
    string BundlePath);

/// <summary>A graded row of a run (C12 <c>rows</c>). <c>SignaturesJson</c> is a JSON array of signature ids.</summary>
public sealed record RowRecord(string RunId, string Section, string RowId, string Grade, string? Reason, long Seed,
    string? KnownIssue, string? SignatureId, string SignaturesJson, string? KnownMatch, string? StartedAt, string? EndedAt);

/// <summary>A fault of a run (C12 <c>faults</c>).</summary>
public sealed record FaultRecord(string RunId, string FaultId, string Kind, string TargetId, string? ArgsJson,
    string? StartedAt, string? HealedAt, string? HealReason);

/// <summary>An invariant violation of a run (C12 <c>invariant_violations</c>).</summary>
public sealed record ViolationRecord(string RunId, string InvariantId, string Service, string CheckId, string SignatureId,
    string At, bool Allowed, string? Section, string? RowId, string? Detail);

/// <summary>An experiment pair of a run (C12 <c>pairs</c>).</summary>
public sealed record PairRecord(string ExperimentId, string RunId, string Role, string PartnerRunId, string Relation, bool? Held);

/// <summary>A signature of the index (C12 <c>signatures</c>).</summary>
public sealed record SignatureRecord(string SignatureId, string Kind, string Text, string SeenAt, string State,
    string? KnownIssueId);

/// <summary>A flake record (C12 <c>flakes</c>).</summary>
public sealed record FlakeRecord(string Section, string RowId, long Seed, string RunIdsJson, string GradesJson, string DetectedAt);
