using System.Text.Json.Nodes;
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
        """
        alter table runs add column known_issues_json text;
        alter table runs add column evidence_json text;
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
              fingerprint_sha256, fidelity, bundle_path, known_issues_json, evidence_json)
            values ($run_id, $kind, $label, $seed, $build_id, $commit_sha, $dirty, $started_at, $ended_at, $overall,
              $fingerprint, $fidelity, $bundle_path, $known_issues_json, $evidence_json)
            on conflict(run_id) do update set kind = excluded.kind, label = excluded.label, seed = excluded.seed,
              build_id = excluded.build_id, commit_sha = excluded.commit_sha, dirty = excluded.dirty,
              started_at = excluded.started_at, ended_at = excluded.ended_at, overall = excluded.overall,
              fingerprint_sha256 = excluded.fingerprint_sha256, fidelity = excluded.fidelity,
              bundle_path = excluded.bundle_path, known_issues_json = excluded.known_issues_json,
              evidence_json = excluded.evidence_json;
            """,
            ("$run_id", run.RunId), ("$kind", run.Kind), ("$label", run.Label), ("$seed", run.Seed),
            ("$build_id", run.BuildId), ("$commit_sha", run.CommitSha), ("$dirty", Flag(run.Dirty)),
            ("$started_at", run.StartedAt), ("$ended_at", run.EndedAt), ("$overall", run.Overall),
            ("$fingerprint", run.FingerprintSha256), ("$fidelity", run.Fidelity), ("$bundle_path", run.BundlePath),
            ("$known_issues_json", run.KnownIssuesJson), ("$evidence_json", run.EvidenceJson));
    }

    /// <summary>
    /// The summary row of one run. Returns <c>[Lab.NotFound]</c> when the run is absent, never a null success (R-RESULT).
    /// </summary>
    /// <param name="runId">The run id.</param>
    public Result<RunRecord> RunOf(string runId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                select run_id, kind, label, seed, build_id, commit_sha, dirty, started_at, ended_at, overall,
                  fingerprint_sha256, fidelity, bundle_path, known_issues_json, evidence_json
                from runs where run_id = $run_id;
                """;
            command.Parameters.AddWithValue("$run_id", runId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Result<RunRecord>.Failure("[Lab.NotFound] no run with that id is in the results database");
            }

            return Result<RunRecord>.Success(new RunRecord(
                reader.GetString(0), reader.GetString(1), TextOrNull(reader, 2), reader.GetInt64(3),
                TextOrNull(reader, 4), TextOrNull(reader, 5), reader.IsDBNull(6) ? null : reader.GetInt64(6) != 0,
                TextOrNull(reader, 7), TextOrNull(reader, 8), TextOrNull(reader, 9), TextOrNull(reader, 10),
                TextOrNull(reader, 11), reader.GetString(12), TextOrNull(reader, 13), TextOrNull(reader, 14)));
        }
        catch (SqliteException ex)
        {
            return Result<RunRecord>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    private static string? TextOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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

    /// <summary>The stored <c>signatures_json</c> of one row, or null when the row is absent.</summary>
    public string? SignaturesJsonOf(string runId, string section, string rowId) =>
        ScalarText(
            "select signatures_json from rows where run_id = $run_id and section = $section and row_id = $row_id;",
            ("$run_id", runId), ("$section", section), ("$row_id", rowId));

    /// <summary>
    /// The grades of one row and seed across every ingested run, with the build each run ran against (flake detection).
    /// </summary>
    /// <param name="section">The row's section.</param>
    /// <param name="rowId">The row id.</param>
    /// <param name="seed">The run seed.</param>
    public Result<IReadOnlyList<RowGradeOf>> GradesOf(string section, string rowId, long seed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var grades = new List<RowGradeOf>();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                select rows.run_id, rows.grade, runs.build_id from rows join runs on runs.run_id = rows.run_id
                where rows.section = $section and rows.row_id = $row_id and rows.seed = $seed order by rows.run_id;
                """;
            command.Parameters.AddWithValue("$section", section);
            command.Parameters.AddWithValue("$row_id", rowId);
            command.Parameters.AddWithValue("$seed", seed);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                grades.Add(new RowGradeOf(reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            return Result<IReadOnlyList<RowGradeOf>>.Success(grades);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<RowGradeOf>>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

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

    /// <summary>The views <see cref="QueryView"/> reads.</summary>
    public static readonly IReadOnlyList<string> Views = ["runs", "rows", "buckets", "flakes", "trend", "known_issues", "findings"];

    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    /// <summary>
    /// Reads one view of the index for <c>results_query</c>. Every filter value is a bound parameter; the SQL is a literal per
    /// view. Returns <c>[Lab.InvalidArgument]</c> for an unknown view or filter key. <c>known_issues</c> here lists the signatures
    /// linked to an issue; the tool adds the shipped file's issues.
    /// </summary>
    /// <param name="view">One of <see cref="Views"/>.</param>
    /// <param name="filter">Filter keys by view (strings); null or empty means no filter.</param>
    /// <param name="limit">The row limit (default 50, max 500).</param>
    public Result<IReadOnlyList<JsonObject>> QueryView(string view, IReadOnlyDictionary<string, string>? filter, int? limit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        filter ??= new Dictionary<string, string>();
        if (limit is < 1 or > MaxLimit)
        {
            return Result<IReadOnlyList<JsonObject>>.Failure("[Lab.InvalidArgument] limit must be between 1 and " + MaxLimit);
        }

        string[] allowed;
        string sql;
        switch (view)
        {
            case "runs":
                allowed = ["run_id", "kind", "overall", "label"];
                sql = """
                    select run_id, kind, label, seed, build_id, commit_sha, dirty, started_at, ended_at, overall, fidelity,
                      known_issues_json, evidence_json from runs
                    where ($run_id is null or run_id = $run_id) and ($kind is null or kind = $kind)
                      and ($overall is null or overall = $overall) and ($label is null or label = $label)
                    order by coalesce(started_at, '') desc, run_id limit $limit;
                    """;
                break;
            case "rows":
                allowed = ["run_id", "section", "row_id", "grade"];
                sql = """
                    select run_id, section, row_id, grade, reason, seed, known_issue, signature_id, signatures_json, known_match
                    from rows
                    where ($run_id is null or run_id = $run_id) and ($section is null or section = $section)
                      and ($row_id is null or row_id = $row_id) and ($grade is null or grade = $grade)
                    order by run_id desc, section, row_id limit $limit;
                    """;
                break;
            case "buckets":
                allowed = ["kind", "state", "signature_id"];
                sql = """
                    select signature_id, kind, text, first_seen, last_seen, hits, state, known_issue_id from signatures
                    where ($kind is null or kind = $kind) and ($state is null or state = $state)
                      and ($signature_id is null or signature_id = $signature_id)
                    order by hits desc, signature_id limit $limit;
                    """;
                break;
            case "flakes":
                allowed = ["section", "row_id"];
                sql = """
                    select section, row_id, seed, run_ids_json, grades_json, detected_at from flakes
                    where ($section is null or section = $section) and ($row_id is null or row_id = $row_id)
                    order by detected_at desc, section, row_id limit $limit;
                    """;
                break;
            case "trend":
                return Trend(filter, limit ?? DefaultLimit);
            case "known_issues":
                allowed = ["known_issue_id"];
                sql = """
                    select known_issue_id, count(*) as signatures, sum(hits) as hits, max(last_seen) as last_seen from signatures
                    where known_issue_id is not null and ($known_issue_id is null or known_issue_id = $known_issue_id)
                    group by known_issue_id order by known_issue_id limit $limit;
                    """;
                break;
            case "findings":
                allowed = ["state", "signature_id"];
                sql = """
                    select finding_id, signature_id, first_run_id, state, created_at from findings
                    where ($state is null or state = $state) and ($signature_id is null or signature_id = $signature_id)
                    order by created_at desc, finding_id limit $limit;
                    """;
                break;
            default:
                return Result<IReadOnlyList<JsonObject>>.Failure("[Lab.InvalidArgument] unknown view; use one of " + string.Join(", ", Views));
        }

        string? bad = UnknownKey(filter, allowed);
        if (bad is not null)
        {
            return Result<IReadOnlyList<JsonObject>>.Failure(bad);
        }

        var parameters = new List<(string Name, object? Value)> { ("$limit", (long)(limit ?? DefaultLimit)) };
        foreach (string key in allowed)
        {
            parameters.Add(("$" + key, filter.TryGetValue(key, out string? value) ? value : null));
        }

        return ReadObjects(sql, parameters.ToArray());
    }

    private Result<IReadOnlyList<JsonObject>> Trend(IReadOnlyDictionary<string, string> filter, int limit)
    {
        string? bad = UnknownKey(filter, ["section", "row_id", "signature_id"]);
        if (bad is not null)
        {
            return Result<IReadOnlyList<JsonObject>>.Failure(bad);
        }

        if (filter.TryGetValue("signature_id", out string? signatureId))
        {
            return ReadObjects(
                """
                select substr(coalesce(runs.started_at, ''), 1, 10) as date, rows.run_id, count(*) as hits
                from rows join runs on runs.run_id = rows.run_id
                where instr(rows.signatures_json, char(34) || $signature_id || char(34)) > 0
                group by rows.run_id order by coalesce(runs.started_at, '') desc, rows.run_id limit $limit;
                """,
                ("$signature_id", signatureId), ("$limit", (long)limit));
        }

        if (filter.TryGetValue("section", out string? section) && filter.TryGetValue("row_id", out string? rowId))
        {
            return ReadObjects(
                """
                select substr(coalesce(runs.started_at, ''), 1, 10) as date, rows.run_id, rows.grade
                from rows join runs on runs.run_id = rows.run_id
                where rows.section = $section and rows.row_id = $row_id
                order by coalesce(runs.started_at, '') desc, rows.run_id limit $limit;
                """,
                ("$section", section), ("$row_id", rowId), ("$limit", (long)limit));
        }

        return Result<IReadOnlyList<JsonObject>>.Failure("[Lab.InvalidArgument] trend needs filter signature_id, or section and row_id");
    }

    private static string? UnknownKey(IReadOnlyDictionary<string, string> filter, string[] allowed)
    {
        foreach (string key in filter.Keys)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal))
            {
                return "[Lab.InvalidArgument] unknown filter key; use one of " + string.Join(", ", allowed);
            }
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The SQL is a literal in this class; every value is bound as a parameter.")]
    private Result<IReadOnlyList<JsonObject>> ReadObjects(string sql, params (string Name, object? Value)[] parameters)
    {
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            var items = new List<JsonObject>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = new JsonObject();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    object raw = reader.GetValue(i);
                    item[reader.GetName(i)] = raw switch
                    {
                        DBNull => null,
                        long number => JsonValue.Create(number),
                        double real => JsonValue.Create(real),
                        string text => JsonValue.Create(text),
                        _ => JsonValue.Create(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)),
                    };
                }

                items.Add(item);
            }

            return Result<IReadOnlyList<JsonObject>>.Success(items);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<JsonObject>>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
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
    /// <summary>
    /// The signature ids of one run that are still new (no known issue) and have no finding yet (L26 triage). Read from the
    /// run's rows, so a signature of an earlier run is not offered again once it has a finding.
    /// </summary>
    /// <param name="runId">The run whose rows are searched.</param>
    public Result<IReadOnlyList<string>> UnfoundedNewSignaturesOfRun(string runId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                select distinct signatures.signature_id from signatures
                join rows on rows.run_id = $run_id and rows.signatures_json like '%"' || signatures.signature_id || '"%'
                where signatures.state = 'new'
                  and not exists (select 1 from findings where findings.signature_id = signatures.signature_id)
                order by signatures.signature_id;
                """;
            command.Parameters.AddWithValue("$run_id", runId);
            var ids = new List<string>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return Result<IReadOnlyList<string>>.Success(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    /// <summary>
    /// Opens a finding (one per signature; the bucket id is the signature id) in state <c>new</c>. An existing finding is kept.
    /// </summary>
    public Result InsertFinding(string signatureId, string firstRunId, string? reproPath, string createdAt) =>
        Execute(
            """
            insert or ignore into findings (finding_id, signature_id, first_run_id, repro_path, draft_path, state, created_at)
            values ($id, $id, $run_id, $repro_path, null, 'new', $created_at);
            """,
            ("$id", signatureId), ("$run_id", firstRunId), ("$repro_path", reproPath), ("$created_at", createdAt));

    /// <summary>Sets a signature's known issue id (the triage link to the shipped known-issue file).</summary>
    public Result SetSignatureKnownIssue(string signatureId, string knownIssueId) =>
        Execute(
            "update signatures set known_issue_id = $known_issue_id, state = 'known' where signature_id = $id;",
            ("$id", signatureId), ("$known_issue_id", knownIssueId));

    /// <summary>Sets a signature's triage state and the issue URL it was filed as (null when none).</summary>
    public Result SetSignatureTriage(string signatureId, string state, string? issueLink) =>
        Execute(
            "update signatures set state = $state, issue_url = $issue_url where signature_id = $id;",
            ("$id", signatureId), ("$state", state), ("$issue_url", issueLink));

    /// <summary>Records the draft path of a finding and moves it to state <c>drafted</c>.</summary>
    public Result SetFindingDraft(string signatureId, string draftPath) =>
        Execute(
            "update findings set draft_path = $draft_path, state = 'drafted' where finding_id = $id;",
            ("$id", signatureId), ("$draft_path", draftPath));

    /// <summary>One bucket of the triage ledger: a signature with its finding (when one is open).</summary>
    /// <param name="SignatureId">The signature id (the bucket id).</param>
    /// <param name="Kind">The signature kind.</param>
    /// <param name="FirstSeen">The first time the signature was seen.</param>
    /// <param name="LastSeen">The last time the signature was seen.</param>
    /// <param name="Hits">The number of graded rows whose signatures include it.</param>
    /// <param name="State">The signature state (<c>new</c>, <c>known</c>, <c>regressed</c>, or a triage state).</param>
    /// <param name="KnownIssueId">The shipped known-issue id, or null.</param>
    /// <param name="IssueLink">The filed issue link, or null.</param>
    /// <param name="FindingState">The finding state, or null when no finding is open.</param>
    /// <param name="ReproPath">The repro file path of the finding, or null.</param>
    /// <param name="DraftPath">The draft path of the finding, or null.</param>
    public sealed record BucketRecord(string SignatureId, string Kind, string FirstSeen, string LastSeen, long Hits, string State,
        string? KnownIssueId, string? IssueLink, string? FindingState, string? ReproPath, string? DraftPath);

    /// <summary>Every bucket of the index, ordered by signature id.</summary>
    public Result<IReadOnlyList<BucketRecord>> LedgerRows() => ReadBuckets(null);

    /// <summary>One bucket by signature id, or a not-found failure.</summary>
    public Result<BucketRecord> BucketOf(string signatureId)
    {
        Result<IReadOnlyList<BucketRecord>> read = ReadBuckets(signatureId);
        if (read.IsFailure)
        {
            return Result<BucketRecord>.Failure(read.Error);
        }

        return read.Value.Count == 0
            ? Result<BucketRecord>.Failure("[Lab.NotFound] no bucket with that id is in the results database")
            : Result<BucketRecord>.Success(read.Value[0]);
    }

    /// <summary>The run ids whose rows carry one signature, ordered by run id.</summary>
    public Result<IReadOnlyList<string>> RunIdsOfSignature(string signatureId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                select distinct run_id from rows where signatures_json like '%"' || $id || '"%' order by run_id;
                """;
            command.Parameters.AddWithValue("$id", signatureId);
            var ids = new List<string>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return Result<IReadOnlyList<string>>.Success(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<string>>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    /// <summary>The stored text of one signature, or a not-found failure.</summary>
    public Result<string> SignatureTextOf(string signatureId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "select text from signatures where signature_id = $id;";
            command.Parameters.AddWithValue("$id", signatureId);
            object? value = command.ExecuteScalar();
            return value is string text
                ? Result<string>.Success(text)
                : Result<string>.Failure("[Lab.NotFound] no bucket with that id is in the results database");
        }
        catch (SqliteException ex)
        {
            return Result<string>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    private Result<IReadOnlyList<BucketRecord>> ReadBuckets(string? signatureId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                """
                select s.signature_id, s.kind, s.first_seen, s.last_seen, s.hits, s.state, s.known_issue_id, s.issue_url,
                  f.state, f.repro_path, f.draft_path
                from signatures s left join findings f on f.signature_id = s.signature_id
                where $id is null or s.signature_id = $id
                order by s.signature_id;
                """;
            command.Parameters.AddWithValue("$id", (object?)signatureId ?? DBNull.Value);
            var buckets = new List<BucketRecord>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                buckets.Add(new BucketRecord(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                    reader.GetString(5), TextOrNull(reader, 6), TextOrNull(reader, 7), TextOrNull(reader, 8),
                    TextOrNull(reader, 9), TextOrNull(reader, 10)));
            }

            return Result<IReadOnlyList<BucketRecord>>.Success(buckets);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<BucketRecord>>.Failure("[Lab.IoError] results database read failed: SQLite " + ex.SqliteErrorCode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();

        // A disposed SqliteConnection returns to the pool and keeps the WAL file open; clearing its pool releases the file.
        SqliteConnection.ClearPool(_connection);
    }
}

/// <summary>A run row of the index (C12 <c>runs</c>).</summary>
public sealed record RunRecord(string RunId, string Kind, string? Label, long Seed, string? BuildId, string? CommitSha,
    bool? Dirty, string? StartedAt, string? EndedAt, string? Overall, string? FingerprintSha256, string? Fidelity,
    string BundlePath, string? KnownIssuesJson = null, string? EvidenceJson = null);

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

/// <summary>One run's grade of a row and seed, with the build that run used (<see cref="ResultsDb.GradesOf"/>).</summary>
public sealed record RowGradeOf(string RunId, string Grade, string? BuildId);
