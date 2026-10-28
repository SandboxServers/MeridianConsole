using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Results;

/// <summary>What one ingest wrote: the run id, its overall, and the number of rows and signatures of the run.</summary>
public sealed record IngestResult(string RunId, string Overall, int Rows, int Signatures, KnownIssueCheck KnownIssues);

/// <summary>
/// Reads one evidence bundle (C9) and writes it to the results index (C12). Ingest is an upsert by <c>run_id</c> (A2): a second
/// ingest replaces the run's rows, violations, <c>known_match</c> and summary. Only the bundle folder under the lab roots is read,
/// and every path passes through <see cref="IPathValidator"/>. Every bundle text stored in the index passes through the scrub
/// function first (R-LOG). Flakes are detected on ingest. Observers (A3) run after a successful write.
/// </summary>
public sealed class BundleIngestor
{
    private const int MaxRunIdLength = 128;
    private const string DefaultKind = "uat";

    private readonly ResultsDb _db;
    private readonly IPathValidator _validator;
    private readonly KnownIssueCheck _known;
    private readonly Func<string, string> _scrub;
    private readonly IReadOnlyList<IIngestObserver> _observers;
    private readonly ILogger<BundleIngestor> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates an ingestor over an open results database.
    /// </summary>
    /// <param name="db">The results database.</param>
    /// <param name="validator">Validates every path the ingest reads.</param>
    /// <param name="known">The known-issue load outcome; its issues are matched against every failing row, and an unchecked outcome matches none.</param>
    /// <param name="scrub">Redacts a bundle text before it is stored (the bundle's <c>Redactor.Redact</c>).</param>
    /// <param name="observers">The observers called after each successful ingest, in order.</param>
    /// <param name="logger">The logger for observer failures; names and type names only.</param>
    /// <param name="time">The clock for flake detection times.</param>
    public BundleIngestor(ResultsDb db, IPathValidator validator, KnownIssueCheck known, Func<string, string> scrub,
        IEnumerable<IIngestObserver> observers, ILogger<BundleIngestor>? logger = null, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _known = known ?? throw new ArgumentNullException(nameof(known));
        _scrub = scrub ?? throw new ArgumentNullException(nameof(scrub));
        ArgumentNullException.ThrowIfNull(observers);
        _observers = observers.ToList();
        _logger = logger ?? NullLogger<BundleIngestor>.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Ingests the bundle folder <paramref name="bundleFolder"/>. Returns a failure, never throws, for a path outside
    /// <paramref name="allowedRoots"/>, a missing or unreadable file, or a database error.
    /// </summary>
    /// <param name="bundleFolder">The bundle folder (under <c>LabPaths.UatRuns</c> or <c>LabPaths.Jobs</c>).</param>
    /// <param name="allowedRoots">The roots a bundle may be read from.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<IngestResult>> IngestAsync(string bundleFolder, IReadOnlyList<string> allowedRoots, CancellationToken ct)
    {
        Result<IngestResult> written = await WriteAsync(bundleFolder, allowedRoots, ct).ConfigureAwait(false);
        if (written.IsSuccess)
        {
            await NotifyAsync(written.Value.RunId, ct).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>
    /// Writes the bundle folder <paramref name="bundleFolder"/> to the index without notifying observers. Returns a failure,
    /// never throws. <see cref="ResultsService"/> calls this under its gate and <see cref="NotifyAsync"/> after the gate is released.
    /// </summary>
    internal async Task<Result<IngestResult>> WriteAsync(string bundleFolder, IReadOnlyList<string> allowedRoots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);
        if (string.IsNullOrWhiteSpace(bundleFolder))
        {
            return Result<IngestResult>.Failure("[Lab.InvalidArgument] the bundle folder is required");
        }

        Result<string> folder = _validator.ValidatePath(bundleFolder, allowedRoots);
        if (folder.IsFailure || !Directory.Exists(folder.Value))
        {
            return Result<IngestResult>.Failure("[Lab.NotFound] no bundle folder under the lab roots");
        }

        Result<string> runPath = BundleFolders.ResolveFile(folder.Value, "run.json", _validator);
        if (runPath.IsFailure)
        {
            return Result<IngestResult>.Failure(runPath.Error);
        }

        Result<JsonObject> run = await ReadObjectAsync(runPath.Value, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<IngestResult>.Failure(run.Error);
        }

        return await IngestRunAsync(folder.Value, run.Value, ct).ConfigureAwait(false);
    }

    /// <summary>Calls the observers for one written run (A3). Never fails; an observer failure is logged by type name.</summary>
    internal Task NotifyAsync(string runId, CancellationToken ct) => NotifyObserversAsync(runId, ct);

    private async Task<Result<IngestResult>> IngestRunAsync(string folder, JsonObject run, CancellationToken ct)
    {
        string? runId = Text(run, "run_id");
        if (string.IsNullOrEmpty(runId) || runId.Length > MaxRunIdLength)
        {
            return Result<IngestResult>.Failure("[Lab.InvalidArgument] run.json has no valid run_id");
        }

        Result<List<ParsedRow>> rows = await ReadRowsAsync(folder, ct).ConfigureAwait(false);
        if (rows.IsFailure)
        {
            return Result<IngestResult>.Failure(rows.Error);
        }

        var graded = new List<GradedRow>();
        var matches = new Dictionary<string, KnownMatch>(StringComparer.Ordinal);
        var rowRecords = new List<RowRecord>();
        var violationRecords = new List<ViolationRecord>();
        var signatureRecords = new Dictionary<string, SignatureRecord>(StringComparer.Ordinal);
        string runEnd = Text(run, "ended_at") ?? string.Empty;

        foreach (ParsedRow row in rows.Value)
        {
            graded.Add(new GradedRow(row.Section, row.RowId, row.Grade));
            string? primaryId = null;
            var signatureIds = new List<string>();
            string? knownMatch = null;

            if (row.Grade is RowGrade.Fail or RowGrade.Blocked or RowGrade.Unverified)
            {
                var evidence = new RowEvidence(row.Section, row.RowId, [], [], row.Clauses,
                    row.Violations.Select(v => v.Evidence).ToList(), [], []);
                List<FailureSignature> scrubbed = SignatureBuilder.BuildAll(evidence).Select(Scrubbed).ToList();
                foreach (FailureSignature signature in scrubbed)
                {
                    string id = SignatureBuilder.SignatureId(signature.Text);
                    if (!signatureIds.Contains(id, StringComparer.Ordinal))
                    {
                        signatureIds.Add(id);
                    }

                    signatureRecords[id] = new SignatureRecord(id, signature.Kind, signature.Text, row.EndedAt ?? runEnd,
                        "new", null);
                }

                FailureSignature? primary = SignatureBuilder.Primary(evidence) is { } first ? Scrubbed(first) : null;
                primaryId = primary is null ? null : SignatureBuilder.SignatureId(primary.Text);

                KnownMatch match = _known.Issues.Match(new RowFailure(row.Section, row.RowId, row.KnownIssue, scrubbed));
                matches[row.Section + "/" + row.RowId] = match;
                knownMatch = match.State switch
                {
                    "known" => match.IssueId,
                    "claimed_mismatch" => "claimed_mismatch",
                    "regressed" => "regressed:" + match.IssueId,
                    _ => null,
                };
                RecordSignatureState(signatureRecords, signatureIds, match, row.EndedAt ?? runEnd);
            }

            rowRecords.Add(new RowRecord(runId, row.Section, row.RowId, LedgerRenderer.ResultText(row.Grade),
                row.Reason is null ? null : _scrub(row.Reason), ParsedSeed(run), row.KnownIssue, primaryId,
                JsonSerializer.Serialize(signatureIds), knownMatch, row.StartedAt, row.EndedAt));

            foreach (ParsedViolation violation in row.Violations)
            {
                FailureSignature invariant = Scrubbed(SignatureBuilder.Invariant(violation.Evidence.InvariantId,
                    violation.Evidence.Service, violation.Evidence.CheckId));
                violationRecords.Add(new ViolationRecord(runId, violation.Evidence.InvariantId, violation.Evidence.Service,
                    violation.Evidence.CheckId, SignatureBuilder.SignatureId(invariant.Text),
                    violation.At ?? row.EndedAt ?? runEnd, violation.Evidence.Allowed, row.Section, row.RowId,
                    violation.Detail is null ? null : _scrub(violation.Detail)));
            }
        }

        RunSummary summary = RunSummary.Compute(graded, matches);
        string overall = summary.OverallText;
        Result write = WriteRun(runId, run, folder, overall, rowRecords, violationRecords, signatureRecords);
        if (write.IsFailure)
        {
            return Result<IngestResult>.Failure(write.Error);
        }

        Result flakes = DetectFlakes(Text(run, "build", "id"), ParsedSeed(run), rows.Value);
        if (flakes.IsFailure)
        {
            return Result<IngestResult>.Failure(flakes.Error);
        }

        return Result<IngestResult>.Success(new IngestResult(runId, overall, rowRecords.Count, signatureRecords.Count, _known));
    }

    private Result WriteRun(string runId, JsonObject run, string folder, string overall, List<RowRecord> rowRecords,
        List<ViolationRecord> violationRecords, Dictionary<string, SignatureRecord> signatureRecords)
    {
        var record = new RunRecord(
            RunId: runId,
            Kind: Text(run, "kind") ?? DefaultKind,
            Label: Text(run, "label") is { } label ? _scrub(label) : null,
            Seed: ParsedSeed(run),
            BuildId: Text(run, "build", "id"),
            CommitSha: Text(run, "build", "commit"),
            Dirty: Bool(run, "build", "dirty"),
            StartedAt: Text(run, "started_at"),
            EndedAt: Text(run, "ended_at"),
            Overall: overall,
            FingerprintSha256: Text(run, "fingerprint_sha256"),
            Fidelity: Text(run, "fidelity"),
            BundlePath: folder);

        Result result = _db.UpsertRun(record);
        if (result.IsFailure)
        {
            return result;
        }

        result = _db.DeleteRunDetails(runId);
        if (result.IsFailure)
        {
            return result;
        }

        foreach (RowRecord row in rowRecords)
        {
            result = _db.InsertRow(row);
            if (result.IsFailure)
            {
                return result;
            }
        }

        foreach (ViolationRecord violation in violationRecords)
        {
            result = _db.InsertViolation(violation);
            if (result.IsFailure)
            {
                return result;
            }
        }

        foreach (SignatureRecord signature in signatureRecords.Values)
        {
            result = _db.UpsertSignature(signature);
            if (result.IsFailure)
            {
                return result;
            }
        }

        // Hits count the rows just written, so the recount runs after every row insert.
        foreach (string signatureId in signatureRecords.Keys)
        {
            result = _db.RecountSignatureHits(signatureId);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return result;
    }

    private Result DetectFlakes(string? buildId, long seed, IReadOnlyList<ParsedRow> rows)
    {
        string detectedAt = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        foreach (ParsedRow row in rows)
        {
            Result<IReadOnlyList<RowGradeOf>> grades = _db.GradesOf(row.Section, row.RowId, seed);
            if (grades.IsFailure)
            {
                return Result.Failure(grades.Error);
            }

            // Only the runs of this build are compared: a grade change between builds is a regression, not a flake.
            List<RowGradeOf> sameBuild = grades.Value.Where(g => g.BuildId == buildId).ToList();
            if (sameBuild.Select(g => g.Grade).Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            Result write = _db.ReplaceFlake(new FlakeRecord(row.Section, row.RowId, seed,
                JsonSerializer.Serialize(sameBuild.Select(g => g.RunId).ToList()),
                JsonSerializer.Serialize(sameBuild.Select(g => g.Grade).ToList()), detectedAt));
            if (write.IsFailure)
            {
                return write;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Sets the state and known issue of every signature of a failing row from that row's verdict. A row is known only when
    /// the verdict is <c>known</c>; a regressed verdict marks its signatures regressed.
    /// </summary>
    private static void RecordSignatureState(Dictionary<string, SignatureRecord> records, List<string> ids, KnownMatch match,
        string seenAt)
    {
        string state = match.State switch
        {
            "known" => "known",
            "regressed" => "regressed",
            _ => "new",
        };
        string? issue = match.State is "known" or "regressed" ? match.IssueId : null;
        foreach (string id in ids)
        {
            SignatureRecord existing = records[id];
            records[id] = existing with { SeenAt = seenAt, State = state, KnownIssueId = issue };
        }
    }

    private FailureSignature Scrubbed(FailureSignature signature) => signature with { Text = _scrub(signature.Text) };

    [SuppressMessage("Design", "CA1031",
        Justification = "An observer must never fail the ingest (A3); the failure is logged by type name and the ingest still succeeds.")]
    private async Task NotifyObserversAsync(string runId, CancellationToken ct)
    {
        foreach (IIngestObserver observer in _observers)
        {
            try
            {
                await observer.OnIngestedAsync(runId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An observer never fails the ingest (A3). The log names the types only; no message text is logged.
                _logger.LogWarning("Ingest observer {Observer} failed: {Error}", observer.GetType().Name, ex.GetType().Name);
            }
        }
    }

    private async Task<Result<List<ParsedRow>>> ReadRowsAsync(string folder, CancellationToken ct)
    {
        var parsed = new List<ParsedRow>();
        string rowsRoot = Path.Combine(folder, "rows");
        if (!Directory.Exists(rowsRoot))
        {
            return Result<List<ParsedRow>>.Success(parsed);
        }

        foreach (string file in Directory.EnumerateFiles(rowsRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            Result<string> path = _validator.ValidatePath(file, [folder]);
            if (path.IsFailure)
            {
                return Result<List<ParsedRow>>.Failure("[Lab.InvalidArgument] a row file is outside the bundle folder");
            }

            Result<JsonObject> obj = await ReadObjectAsync(path.Value, ct).ConfigureAwait(false);
            if (obj.IsFailure)
            {
                return Result<List<ParsedRow>>.Failure(obj.Error);
            }

            Result<ParsedRow> row = ParseRow(obj.Value);
            if (row.IsFailure)
            {
                return Result<List<ParsedRow>>.Failure(row.Error);
            }

            parsed.Add(row.Value);
        }

        return Result<List<ParsedRow>>.Success(parsed);
    }

    private static async Task<Result<JsonObject>> ReadObjectAsync(string path, CancellationToken ct)
    {
        Result<string> text = await BundleFolders.ReadTextAsync(path, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<JsonObject>.Failure(text.Error);
        }

        try
        {
            return JsonNode.Parse(text.Value) is JsonObject obj
                ? Result<JsonObject>.Success(obj)
                : Result<JsonObject>.Failure("[Lab.InvalidArgument] a bundle file is not a JSON object");
        }
        catch (JsonException)
        {
            return Result<JsonObject>.Failure("[Lab.InvalidArgument] a bundle file is not valid JSON");
        }
    }

    private static Result<ParsedRow> ParseRow(JsonObject obj)
    {
        string? section = Text(obj, "section");
        string? rowId = Text(obj, "id");
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(rowId))
        {
            return Result<ParsedRow>.Failure("[Lab.InvalidArgument] a row file has no section or id");
        }

        var clauses = Objects(obj, "clauses")
            .Where(clause => Bool(clause, "required") ?? true)
            .Select(clause => new ClauseEvidence(section, rowId, Text(clause, "id") ?? string.Empty,
                Text(clause, "state") ?? string.Empty))
            .ToList();
        var violations = Objects(obj, "violations")
            .Select(v => new ParsedViolation(
                new ViolationEvidence(Text(v, "invariant_id") ?? string.Empty, Text(v, "service") ?? string.Empty,
                    Text(v, "check_id") ?? string.Empty, Bool(v, "allowed") ?? false),
                Text(v, "detail"), Text(v, "at")))
            .ToList();

        return Result<ParsedRow>.Success(new ParsedRow(section, rowId, ParseGrade(Text(obj, "grade")), Text(obj, "reason"),
            Text(obj, "started_at"), Text(obj, "ended_at"), Text(obj, "known_issue"), clauses, violations));
    }

    private static RowGrade ParseGrade(string? text) => text switch
    {
        "PASS" => RowGrade.Pass,
        "FAIL" => RowGrade.Fail,
        "BLOCKED" => RowGrade.Blocked,
        "SKIPPED" => RowGrade.Skipped,
        "NEEDS_HUMAN" => RowGrade.NeedsHuman,
        _ => RowGrade.Unverified,
    };

    private static long ParsedSeed(JsonObject run) =>
        run["seed"] is JsonValue value && value.TryGetValue(out long seed) ? seed : 0;

    private static IEnumerable<JsonObject> Objects(JsonObject obj, string key) =>
        (obj[key] as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static string? Text(JsonObject obj, params string[] path)
    {
        JsonNode? node = obj;
        foreach (string key in path)
        {
            node = (node as JsonObject)?[key];
        }

        return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    private static bool? Bool(JsonObject obj, params string[] path)
    {
        JsonNode? node = obj;
        foreach (string key in path)
        {
            node = (node as JsonObject)?[key];
        }

        return node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;
    }

    private sealed record ParsedRow(string Section, string RowId, RowGrade Grade, string? Reason, string? StartedAt,
        string? EndedAt, string? KnownIssue, IReadOnlyList<ClauseEvidence> Clauses, IReadOnlyList<ParsedViolation> Violations);

    private sealed record ParsedViolation(ViolationEvidence Evidence, string? Detail, string? At);
}
