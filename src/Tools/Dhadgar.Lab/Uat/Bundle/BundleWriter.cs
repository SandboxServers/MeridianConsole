using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// Writes the C9 evidence bundle of one run under <c>LabPaths.UatRuns</c>: <c>run.json</c>, <c>rows\&lt;section&gt;\&lt;row&gt;.json</c>,
/// <c>repro\repro.toml</c> and <c>ledger.md</c>. Every file passes through <see cref="Redactor"/>; <see cref="SecretScanner"/> runs
/// last, and a hit forces every row to BLOCKED with reason <c>secret scan failed</c>. A plan-only run writes nothing.
/// Write failures never throw into the runner; they are kept in <see cref="WriteError"/>.
/// </summary>
public sealed class BundleWriter : IUatRunSink
{
    private const string SecretScanFailed = "secret scan failed";
    private const int MaxEvidenceChars = 16384;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly string _uatRuns;
    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;
    private readonly SecretScanner _scanner;
    private readonly TimeProvider _time;
    private string _buildId;
    private readonly string _system;
    private string _build;
    private BuildStamp _stamp;
    private readonly string? _label;
    private readonly List<LedgerRow> _rows = [];
    private readonly List<UatRowResult> _results = [];
    private UatSpec? _spec;
    private string _runId = string.Empty;
    private ulong _seed;
    private DateTimeOffset _started;
    private LabFingerprint? _fingerprint;
    private string _fingerprintSha = string.Empty;
    private readonly List<string> _warnings = [];
    private LiveFingerprintParts? _live;
    private int _passRedactions;
    private int _rowRedactions;
    private IRunLogSource? _logs;

    // L25e: an exception line names its type (Name.Exception), and a request line is the Hosting.Diagnostics finish line.
    private static readonly Regex ExceptionTypeName = new(@"\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*Exception\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex RequestFinished = new(
        @"Request finished (?:HTTP/\S+ )?(?<method>[A-Z]+) (?<url>\S+) - (?<status>\d{3})",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex GuidSegment = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex NumericSegment = new(@"(?<=/)[0-9]+(?=/|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private const int EvidenceTextLimit = 512;
    private string? _finalOverall;
    private KnownIssueCheck _known = KnownIssueCheck.Unchecked(KnownIssueCheck.BundleUncheckedReason);

    /// <summary>
    /// Creates the writer for one run.
    /// </summary>
    /// <param name="uatRuns">The <c>LabPaths.UatRuns</c> root.</param>
    /// <param name="validator">Validates every path before a write.</param>
    /// <param name="redactor">Redacts every written text.</param>
    /// <param name="scanner">The last check before the bundle is final.</param>
    /// <param name="time">The clock.</param>
    /// <param name="buildId">The eight-character build id, or <c>nobuild</c>.</param>
    /// <param name="system">The system line for the ledger.</param>
    /// <param name="build">The build line for the ledger.</param>
    /// <param name="label">The run label, or null.</param>
    public BundleWriter(string uatRuns, IPathValidator validator, Redactor redactor, SecretScanner scanner, TimeProvider time,
        string buildId, string system, string build, string? label)
    {
        _uatRuns = uatRuns ?? throw new ArgumentNullException(nameof(uatRuns));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _buildId = string.IsNullOrWhiteSpace(buildId) ? "nobuild" : buildId;
        _system = system ?? string.Empty;
        _build = build ?? string.Empty;
        _label = label;
        _stamp = new BuildStamp(_buildId, string.Empty, false, string.Empty);
    }

    /// <summary>
    /// The bundle folder once the run finished, or null for a plan-only run or a write failure.
    /// </summary>
    public string? Folder { get; private set; }

    /// <inheritdoc />
    public string? FolderName => string.IsNullOrEmpty(_runId) ? null : BundleFolders.Name(_started, _buildId, _runId);

    /// <inheritdoc />
    public string? FinalOverall => _finalOverall;

    /// <summary>
    /// Sets the build the bundle is written against (folder name, <c>run.json</c> build, fingerprint and ledger line). Call before <see cref="RunStarted"/>.
    /// </summary>
    /// <param name="stamp">The build stamp.</param>
    public void UseBuild(BuildStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        _stamp = stamp;
        _buildId = stamp.Id;
        _build = stamp.LedgerLine;
    }

    /// <summary>
    /// Sets the known-issue file the bundle is matched against (L26 B2). Without this call the bundle is unchecked (D-ML92):
    /// no row is matched and <c>run.json</c> says so.
    /// </summary>
    /// <param name="check">The load outcome, from <c>ResultsService.LoadKnownIssues</c>.</param>
    public void UseKnownIssues(KnownIssueCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _known = check;
    }

    /// <summary>
    /// Supplies the log slices written under <c>logs\</c> for the run window. Without it no log files are written.
    /// </summary>
    /// <param name="source">The log source.</param>
    public void UseLogs(IRunLogSource source)
    {
        _logs = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// The first write failure, as a short text without paths or values, or null.
    /// </summary>
    public string? WriteError { get; private set; }

    /// <inheritdoc />
    public void RunStarted(string runId, ulong seed, UatSpec spec)
    {
        _runId = runId;
        _seed = seed;
        _spec = spec;
        _started = _time.GetUtcNow();
        CaptureFingerprint(seed, spec);
    }

    /// <summary>
    /// Captures the fingerprint at run start. Parts this writer cannot collect become <c>unknown</c> and a warning in run.json.
    /// </summary>
    private void CaptureFingerprint(ulong seed, UatSpec spec)
    {
        var input = new FingerprintInput(
            Commit: _stamp.Commit.Length > 0 ? _stamp.Commit : FingerprintBuilder.UnknownPart,
            Dirty: _stamp.Dirty,
            BuildId: _buildId == "nobuild" ? null : _buildId,
            ImageDigests: _live?.ImageDigests ?? new Dictionary<string, string>(),
            LockFilesSha256: null,
            ServiceEnvironments: new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            SecretKeys: new HashSet<string>(),
            Specs: [new SpecHash(spec.File, spec.Sha256)],
            DaemonVersion: FingerprintBuilder.DaemonVersionText(),
            ToxiproxyVersion: _live?.ToxiproxyVersion,
            Seed: seed,
            Os: RuntimeInformation.OSDescription,
            EngineVersions: _live?.EngineVersions ?? new Dictionary<string, string>());

        _fingerprint = FingerprintBuilder.Build(input);
        _fingerprintSha = FingerprintHasher.Sha256(_fingerprint);
        _warnings.Clear();
        if (_stamp.Commit.Length == 0)
        {
            _warnings.Add("commit not collected: unknown");
        }

        _warnings.Add("lock files not collected: unknown");
        _warnings.AddRange(_live?.Warnings ?? ["live fingerprint parts not collected: unknown"]);
    }

    /// <summary>
    /// Supplies the live fingerprint parts (engine, images, Toxiproxy). Call before the run starts; without it those parts are <c>unknown</c>.
    /// </summary>
    /// <param name="parts">The collected parts.</param>
    public void UseLiveParts(LiveFingerprintParts parts)
    {
        _live = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    private ReplayFidelity CurrentFidelity(bool truncated) => FidelityRules.For(new FidelityInput(
        ConcurrentActors: false,
        TimingFaults: _results.Any(row => row.Faults?.Any(IsTimingFault) == true),
        Truncated: truncated,
        Attested: false,
        BuildPresent: _buildId != "nobuild"));

    /// <summary>
    /// True for a fault that bends time: a <c>fault_toxic_add</c> of <c>latency</c>, <c>bandwidth</c>, <c>slow_close</c> or <c>timeout</c>, or any <c>fault_pause</c>.
    /// </summary>
    private static bool IsTimingFault(UatRowFault fault)
    {
        if (fault.Tool == "fault_pause")
        {
            return true;
        }

        return fault.Tool == "fault_toxic_add"
            && fault.Args.ValueKind == JsonValueKind.Object
            && fault.Args.TryGetProperty("toxic", out JsonElement toxic)
            && toxic.ValueKind == JsonValueKind.String
            && toxic.GetString() is "latency" or "bandwidth" or "slow_close" or "timeout";
    }

    private static string FidelityText(ReplayFidelity fidelity) => fidelity switch
    {
        ReplayFidelity.Exact => "exact",
        ReplayFidelity.ScheduleApproximate => "schedule_approximate",
        _ => "evidence_only",
    };

    /// <inheritdoc />
    public void RowFinished(UatRowResult row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _results.Add(row);
        UatRow? spec = _spec?.Rows.FirstOrDefault(candidate => candidate.Id == row.RowId);
        string section = _spec?.Section.Id ?? string.Empty;
        // Free text is redacted before the renderer rewrites it, so a vault value the renderer would reshape is still matched.
        // The count is kept so that run.json still reports these redactions in the next write pass.
        string Scrub(string text)
        {
            string redacted = _redactor.Redact(text, out int count);
            _rowRedactions += count;
            return redacted;
        }

        var clauses = (spec?.Expect ?? Array.Empty<UatClause>()).Select(clause =>
        {
            UatClauseResult? result = row.Clauses.FirstOrDefault(candidate => candidate.Id == clause.Id);
            string? error = result?.Error is null ? null : Scrub(result.Error);
            return new LedgerClause(clause.Id, Scrub(clause.Pointer ?? clause.Source), result?.State ?? ClauseState.Unverified, error, clause.Source, clause.Required);
        }).ToList();
        _rows.Add(new LedgerRow(section, row.RowId, Scrub(spec?.Title ?? row.RowId), Scrub(spec?.Expected ?? string.Empty), row.Grade, row.EndedAt ?? _time.GetUtcNow(), clauses));
    }

    /// <inheritdoc />
    public void RunFinished(UatRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.PlanOnly || _spec is null)
        {
            return;
        }

        string folder = Path.Combine(_uatRuns, BundleFolders.Name(_started, _buildId, _runId));
        Result<string> checkedFolder = _validator.ValidatePath(folder, [_uatRuns]);
        if (checkedFolder.IsFailure)
        {
            WriteError = "[Lab.InvalidArgument] the bundle folder is outside the UAT runs root";
            return;
        }

        Folder = checkedFolder.Value;
        try
        {
            WriteAll(result, secretFailed: false, []);
            SecretScanResult scan = _scanner.ScanFolder(Folder);
            if (!scan.Passed)
            {
                // Paths are bundle-relative with forward slashes, whatever the platform separator.
                IReadOnlyList<string> removed = scan.RemovedFiles.Select(file => file.Replace('\\', '/')).ToList();
                WriteAll(result, secretFailed: true, removed);
                if (!_scanner.ScanFolder(Folder).Passed)
                {
                    WriteError = "[Lab.Unavailable] the bundle still held a secret after the rewrite";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            WriteError = "[Lab.Unavailable] the bundle could not be written";
            MarkFailedAfterWriteError(checkedFolder.Value);
        }
    }

    /// <summary>
    /// Writes a history run's bundle (C19): a normal run folder with <c>run.json</c> (<c>kind = history</c>),
    /// <c>history.json</c>, <c>check.json</c>, <c>minimal.json</c> when minimised, and the synthetic row <c>row.json</c>
    /// (<c>history/&lt;scenario&gt;</c>, with <c>history:{verdict, violations:[{rule, ops, required}]}</c>). The folder is checked
    /// against the UAT runs root, and the whole folder is scanned for secrets; a secret fails the write (no rewrite).
    /// </summary>
    /// <returns>The bundle folder, or a failure with a <c>[Lab.*]</c> code.</returns>
    public Result<string> WriteHistoryRun(HistoryBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        string folder = Path.Combine(_uatRuns, BundleFolders.Name(bundle.Started, _buildId, bundle.RunId));
        Result<string> checkedFolder = _validator.ValidatePath(folder, [_uatRuns]);
        if (checkedFolder.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the bundle folder is outside the UAT runs root");
        }

        Folder = checkedFolder.Value;
        const string secretHeld = "[Lab.Unavailable] the history bundle held a secret and was not kept";
        try
        {
            _passRedactions = 0;
            Write("history.json", JsonSerializer.Serialize(bundle.History, JsonOptions) + "\n");
            Write("check.json", JsonSerializer.Serialize(bundle.Check, JsonOptions) + "\n");
            if (bundle.Check.Minimal is not null)
            {
                Write("minimal.json", JsonSerializer.Serialize(bundle.Check.Minimal, JsonOptions) + "\n");
            }

            Write("row.json", JsonSerializer.Serialize(new
            {
                id = "history/" + bundle.Scenario,
                grade = bundle.Grade.ToString(),
                history = new
                {
                    // L25f: the model names the hist signature; every history run is checked against the enrollment model.
                    model = Dhadgar.Lab.History.Models.EnrollmentHistoryModel.ModelName,
                    verdict = bundle.Check.Verdict,
                    violations = bundle.Check.Violations.Select(v => new { rule = v.Rule, ops = v.OpIds, required = v.Required }).ToList(),
                },
            }, JsonOptions) + "\n");

            // Scan the payload before run.json exists in its final form, so run.json never says "passed" for a folder that
            // was not scanned clean. A failed scan marks the folder failed (the scanner has already removed the flagged file).
            SecretScanResult scan = _scanner.ScanFolder(Folder);
            if (!scan.Passed)
            {
                MarkFailedAfterWriteError(Folder);
                return Result<string>.Failure(secretHeld);
            }

            Write("run.json", HistoryRunJson(bundle, "passed"));
            if (!_scanner.ScanFolder(Folder).Passed)
            {
                MarkFailedAfterWriteError(Folder);
                return Result<string>.Failure(secretHeld);
            }

            return Result<string>.Success(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            WriteError = "[Lab.Unavailable] the bundle could not be written";
            MarkFailedAfterWriteError(Folder);
            return Result<string>.Failure(WriteError);
        }
    }

    private static string HistoryRunJson(HistoryBundle bundle, string secretScan) => JsonSerializer.Serialize(new
    {
        run_id = bundle.RunId,
        kind = "history",
        scenario = bundle.Scenario,
        seed = bundle.Seed,
        clients = bundle.Clients,
        reruns = bundle.Reruns,
        verdict = bundle.Check.Verdict,
        grade = bundle.Grade.ToString(),
        secret_scan = secretScan,
    }, JsonOptions) + "\n";

    /// <summary>
    /// Runs the scan after a failed write, so a half-written bundle is not left unscanned, then records
    /// <c>secret_scan = failed</c> with text that carries no evidence.
    /// </summary>
    private void MarkFailedAfterWriteError(string folder)
    {
        try
        {
            _ = _scanner.ScanFolder(folder);
            Write("run.json", "{\n  \"secret_scan\": \"failed\"\n}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteError = "[Lab.Unavailable] the bundle could not be written";
        }
    }

    private void WriteAll(UatRunResult result, bool secretFailed, IReadOnlyList<string> removed)
    {
        _passRedactions = _rowRedactions;
        UatSpec spec = _spec!;
        IReadOnlyList<LedgerRow> rows = secretFailed
            ? _rows.Select(row => row with { Grade = RowGrade.Blocked, Title = row.Row, Expected = string.Empty, Clauses = Array.Empty<LedgerClause>() }).ToList()
            : _rows;
        string secretScan = secretFailed ? "failed" : "passed";

        var summary = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["PASS"] = 0, ["FAIL"] = 0, ["BLOCKED"] = 0, ["SKIPPED"] = 0, ["NEEDS_HUMAN"] = 0, ["UNVERIFIED"] = 0,
        };
        foreach (LedgerRow row in rows)
        {
            summary[LedgerRenderer.ResultText(row.Grade)]++;
        }

        if (_fingerprint is not null && !secretFailed)
        {
            Write("fingerprint.json", JsonSerializer.Serialize(_fingerprint, JsonOptions));
        }

        // L26 B2: every row is matched against the known issues as the ingest matches it (RowVerdicts), and the verdicts feed
        // run.json below. A secret-failed pass matches nothing: its rows are all BLOCKED and its overall is "fail".
        var graded = new List<GradedRow>();
        var matches = new Dictionary<string, KnownMatch>(StringComparer.Ordinal);
        foreach (LedgerRow row in rows)
        {
            UatRowResult? source = _results.FirstOrDefault(candidate => candidate.RowId == row.Row);
            var clauseJson = row.Clauses.Select(clause =>
            {
                UatClauseResult? evidence = source?.Clauses.FirstOrDefault(candidate => candidate.Id == clause.Id);
                UatClause? specClause = spec.Rows.FirstOrDefault(candidate => candidate.Id == row.Row)?.Expect
                    .FirstOrDefault(candidate => candidate.Id == clause.Id);
                string clauseText = string.Empty;
                if (specClause is not null)
                {
                    clauseText = _redactor.Redact(specClause.Text, out int textRedactions);
                    clauseText = SensitiveText.Redact(clauseText, out int pairRedactions);
                    _passRedactions += textRedactions + pairRedactions;
                }

                return new
                {
                    id = clause.Id,
                    text = clauseText,
                    pointer = clause.Locator,
                    source = clause.Source,
                    op = specClause?.Op,
                    value = Evidence(specClause?.Value),
                    required = clause.Required,
                    state = clause.State.ToString(),
                    error = clause.Error,
                    observed = Evidence(evidence?.Observed),
                    attempts = evidence?.Attempts ?? 0,
                    checked_at = evidence?.CheckedAt,
                };
            }).ToList();
            var stepJson = new List<object>();
            if (!secretFailed && source?.Steps is { } recorded)
            {
                foreach (UatStepResult step in recorded)
                {
                    JsonNode stepNode = JsonSerializer.SerializeToNode(new
                    {
                        id = step.Id,
                        tool = step.Tool,
                        ok = step.Ok,
                        error = step.Error,
                        error_code = step.ErrorCode,
                        expected_error = step.ExpectedError,
                        duration_ms = step.DurationMs,
                        result = Evidence(step.Result),
                    }, JsonOptions)!;
                    // L26 B3: expect_met is written only when the step declared an expected error.
                    if (step.ExpectedError is { } expectedError)
                    {
                        stepNode["expect_met"] = RowExecutor.ExpectedErrorMet(expectedError, step.ErrorCode, step.Result);
                    }

                    stepJson.Add(stepNode);
                }
            }
            var faultJson = new List<object>();
            var violationJson = new List<object>();
            if (!secretFailed)
            {
                foreach (UatRowFault fault in source?.Faults ?? Array.Empty<UatRowFault>())
                {
                    faultJson.Add(new
                    {
                        id = fault.Id,
                        tool = fault.Tool,
                        fault_id = fault.FaultId,
                        kind = fault.Kind,
                        target_id = fault.TargetId,
                        injected_at = fault.InjectedAt,
                        healed_at = fault.HealedAt,
                        heal_error = fault.HealError is null ? null : _redactor.Redact(fault.HealError, out int healRedactions),
                    });
                }

                foreach (UatRowViolation violation in source?.Violations ?? Array.Empty<UatRowViolation>())
                {
                    violationJson.Add(new
                    {
                        invariant_id = violation.Violation.InvariantId,
                        service = violation.Violation.Service,
                        check_id = violation.Violation.CheckId,
                        detail = violation.Violation.Detail,
                        at = violation.Violation.At,
                        allowed = violation.Allowed,
                    });
                }
            }

            string reason = secretFailed ? SecretScanFailed : source?.Reason ?? string.Empty;
            var rowJson = new
            {
                section = row.Section,
                id = row.Row,
                title = row.Title,
                expected = row.Expected,
                grade = LedgerRenderer.ResultText(row.Grade),
                reason,
                started_at = source?.StartedAt ?? row.At,
                ended_at = row.At,
                member = source?.Member,
                clauses = secretFailed ? Array.Empty<object>() : clauseJson.Cast<object>(),
                steps = stepJson,
                faults = faultJson,
                violations = violationJson,
                evidence = RowEvidenceJson(source, row.At, secretFailed),
                attestations = Array.Empty<object>(),
            };
            // The row file is parsed back by the ingest's reader, so the verdict here is the verdict the DB records.
            JsonObject node = JsonSerializer.SerializeToNode(rowJson, JsonOptions)!.AsObject();
            node["known_issue"] = spec.Rows.FirstOrDefault(candidate => candidate.Id == row.Row)?.KnownIssue;
            Result<ParsedRow> parsed = RowVerdicts.Parse(node);
            RowVerdict verdict = secretFailed || parsed.IsFailure
                ? new RowVerdict([], [], null, null, null)
                : RowVerdicts.Of(parsed.Value!, _known.Issues, text => _redactor.Redact(text, out _));
            node["known_match"] = _known.Checked && verdict.Match is { } match
                ? new JsonObject { ["issue_id"] = match.IssueId, ["state"] = match.State, ["reason"] = match.Reason }
                : null;
            graded.Add(new GradedRow(row.Section, row.Row, row.Grade));
            if (verdict.Match is { } found)
            {
                matches[row.Section + "/" + row.Row] = found;
            }

            Write(Path.Combine("rows", row.Section, row.Row + ".json"), node.ToJsonString(JsonOptions));
        }

        // A1: the one rule (RunSummary). A secret-failed pass is "fail" whatever the rows say, as before.
        RunSummary runSummary = RunSummary.Compute(graded, matches);
        string overall = secretFailed ? "fail" : runSummary.OverallText;
        _finalOverall = overall;

        if (!secretFailed)
        {
            // Violations and check errors are already redacted by their invariants.
            InvariantReport invariants = result.Invariants ?? new InvariantReport([], []);
            Write("invariants.json", JsonSerializer.Serialize(new { schema = 1, violations = invariants.Violations, errors = invariants.Errors, skipped = invariants.Skipped ?? [] }, JsonOptions));
        }

        Write(Path.Combine("repro", "repro.toml"), ReproToml(spec, result.Seed, CurrentFidelity(result.Truncated)));
        Write("ledger.md", LedgerRenderer.Render(_runId, _system, _build, rows));
        if (!secretFailed)
        {
            WriteLogs();
        }

        // The distinct tools of the recorded steps; none in the secret-failed pass, which writes no steps.
        string[] tools = secretFailed
            ? []
            : _results.SelectMany(row => row.Steps ?? Array.Empty<UatStepResult>())
                .Where(step => step.Tool is not null)
                .Select(step => step.Tool!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

        // A skip outside (run-start) and stack-up is a source that was not evaluated after the stack existed. It never changes
        // a grade (D-ML80), but run.json and warnings name it so that a ledger reader does not read "clean" for it.
        IReadOnlyList<InvariantSkip> skips = result.Invariants?.Skipped ?? [];
        IReadOnlyList<InvariantSkip> unexpectedSkips = skips.Where(skip => skip.Row is not ("(run-start)" or "stack-up")).ToList();
        string[] warnings = _warnings
            .Concat(unexpectedSkips.Select(skip => $"invariant {skip.InvariantId} not evaluated for {skip.Source} at {skip.Row ?? "an unlabelled checkpoint"}: {skip.Reason}"))
            .ToArray();

        // Last, so that redactions counts every other file written in this pass.
        var run = new
        {
            schema = 1,
            run_id = _runId,
            label = secretFailed ? null : _label,
            seed = _seed,
            started_at = _started,
            ended_at = _time.GetUtcNow(),
            daemon_version = typeof(BundleWriter).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            os = RuntimeInformation.OSDescription,
            build = new { id = _stamp.Id, commit = _stamp.Commit, dirty = _stamp.Dirty, checkout = _stamp.Checkout },
            lease_owner = result.LeaseOwner,
            members = Array.Empty<string>(),
            specs = new[] { new { file = spec.File, sha256 = spec.Sha256 } },
            tools,
            summary,
            overall,
            incomplete_reasons = Array.Empty<string>(),
            new_failures = runSummary.NewFailures,
            known_failures = _known.Checked ? runSummary.KnownFailures : null,
            known_issues = _known.Checked ? (object)_known.ToJson() : new { @checked = false, reason = KnownIssueCheck.BundleUncheckedReason },
            violations = secretFailed ? 0 : result.Invariants?.Violations.Count ?? 0,
            row_violations = secretFailed
                ? []
                : _results.SelectMany(row => (row.Violations ?? Array.Empty<UatRowViolation>())
                    .Select(v => new { row = row.RowId, invariant_id = v.Violation.InvariantId, allowed = v.Allowed })).ToArray(),
            redactions = _passRedactions,
            secret_scan = secretScan,
            secret_scan_removed = removed,
            fingerprint_sha256 = _fingerprintSha,
            fidelity = FidelityText(CurrentFidelity(result.Truncated)),
            pair = result.Pair is { } info ? (object)new { experiment_id = info.ExperimentId, role = info.Role, partner_run_id = info.PartnerRunId, relation = info.Relation } : null,
            replay = (object?)null,
            events = result.Events,
            warnings,
            invariant_skips = skips.Count,
            invariant_skips_unevaluated = unexpectedSkips.Select(skip => new { row = skip.Row, source = skip.Source, invariant_id = skip.InvariantId }).ToArray(),
        };

        Write("run.json", JsonSerializer.Serialize(run, JsonOptions));
    }

    /// <summary>
    /// Returns the evidence value redacted, or a truncated stand-in when its text is longer than
    /// <see cref="MaxEvidenceChars"/>. The redactions it makes are added to the pass counter.
    /// </summary>
    private JsonElement? Evidence(JsonElement? element)
    {
        if (element is not { } value)
        {
            return null;
        }

        // Values under a sensitive name (and the value of a sensitive header) are blanked first: the stack can mint
        // tokens and cookies at run time that the vault never held.
        string blanked = BlankSensitive(value, out int pairs);
        _passRedactions += pairs;
        _redactor.Redact(blanked, out int count);
        _passRedactions += count;
        JsonElement redacted;
        using (JsonDocument parsed = JsonDocument.Parse(blanked))
        {
            redacted = _redactor.Redact(parsed.RootElement);
        }

        string text = redacted.GetRawText();
        return text.Length > MaxEvidenceChars
            ? JsonSerializer.SerializeToElement(new { truncated = true, text = text[..MaxEvidenceChars] })
            : redacted;
    }

    /// <summary>
    /// Writes the element as JSON with every scalar under a sensitive key replaced by <c>"[redacted]"</c>. A header
    /// object (one with a sensitive <c>name</c>) has its <c>value</c> replaced too. A string outside a sensitive member has
    /// its <c>name=value</c> credential pairs redacted (<see cref="SensitiveText"/>); <paramref name="pairs"/> counts them.
    /// </summary>
    private static string BlankSensitive(JsonElement element, out int pairs)
    {
        int count = 0;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteBlanked(writer, element, inSensitive: false, ref count);
        }

        pairs = count;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteBlanked(Utf8JsonWriter writer, JsonElement element, bool inSensitive, ref int pairs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                bool sensitiveHeader = element.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && IsSensitiveEvidenceName(name.GetString() ?? string.Empty);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    bool child = inSensitive || IsSensitiveEvidenceName(property.Name)
                        || (sensitiveHeader && property.Name == "value");
                    WriteBlanked(writer, property.Value, child, ref pairs);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteBlanked(writer, item, inSensitive, ref pairs);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String when !inSensitive:
                writer.WriteStringValue(SensitiveText.Redact(element.GetString() ?? string.Empty, out int found));
                pairs += found;
                break;

            default:
                if (inSensitive)
                {
                    writer.WriteStringValue("[redacted]");
                }
                else
                {
                    element.WriteTo(writer);
                }

                break;
        }
    }

    private static bool IsSensitiveEvidenceName(string name)
    {
        foreach (string word in SensitiveText.Words)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes <c>logs\&lt;process&gt;.jsonl</c> for each slice whose process name is a safe id: one compact JSON object per line
    /// with the timestamp, level, category, event id and the message (<see cref="SensitiveText"/> redacted).
    /// </summary>
    private void WriteLogs()
    {
        if (_logs is null)
        {
            return;
        }

        IReadOnlyList<RunLogSlice> slices = _logs.Slices(_started, _time.GetUtcNow());
        foreach (RunLogSlice slice in slices)
        {
            if (!SafeIds.IsSafe(slice.Process))
            {
                continue;
            }

            var text = new StringBuilder();
            foreach (LogLine line in slice.Lines)
            {
                string message = SensitiveText.Redact(line.Message, out int found);
                _passRedactions += found;
                JsonNode? eventId = line.EventId is int id ? JsonValue.Create(id) : null;
                var entry = new JsonObject
                {
                    ["ts"] = line.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    ["level"] = line.Level,
                    ["category"] = line.Category,
                    ["event_id"] = eventId,
                    ["message"] = message,
                };
                text.Append(entry.ToJsonString()).Append('\n');
            }

            Write(Path.Combine("logs", slice.Process + ".jsonl"), text.ToString());
        }
    }

    /// <summary>
    /// L25e: the row's <c>evidence</c>, read from the log window (<c>started_at</c> to <c>ended_at</c>). <c>exceptions</c> are the
    /// Error or Critical exception lines whose message names a type: the type, the top in-repo frame and the first message
    /// line. <c>http</c> are the 5xx Hosting.Diagnostics request lines: the service, status, method, route template and the
    /// fault ids active at that line. Every text is redacted with <see cref="Redactor"/>. A secret-failed row carries none.
    /// </summary>
    private JsonObject RowEvidenceJson(UatRowResult? source, DateTimeOffset rowEnd, bool secretFailed)
    {
        var exceptions = new JsonArray();
        var http = new JsonArray();
        var evidence = new JsonObject { ["exceptions"] = exceptions, ["http"] = http };
        if (secretFailed || _logs is null)
        {
            return evidence;
        }

        DateTimeOffset from = source?.StartedAt ?? rowEnd;
        IReadOnlyList<UatRowFault> faults = source?.Faults ?? Array.Empty<UatRowFault>();
        foreach (RunLogSlice slice in _logs.Slices(from, rowEnd))
        {
            if (!SafeIds.IsSafe(slice.Process))
            {
                continue;
            }

            foreach (LogLine line in slice.Lines)
            {
                if (line.Timestamp < from || line.Timestamp > rowEnd)
                {
                    continue;
                }

                if (IsExceptionLine(line))
                {
                    if (ExceptionEntry(slice.Process, line) is { } entry)
                    {
                        exceptions.Add(entry);
                    }

                    continue;
                }

                if (RequestEntry(slice.Process, line, faults) is { } request)
                {
                    http.Add(request);
                }
            }
        }

        return evidence;
    }

    /// <summary>An Error or Critical line of an exception category, or any line that says it was unhandled.</summary>
    private static bool IsExceptionLine(LogLine line)
    {
        bool isError = string.Equals(line.Level, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(line.Level, "Critical", StringComparison.OrdinalIgnoreCase);
        bool exceptionLog = isError
            && (line.Category.StartsWith("Microsoft.AspNetCore.Diagnostics.", StringComparison.Ordinal)
                || line.Category.Contains("GlobalExceptionHandler", StringComparison.Ordinal));
        return exceptionLog || line.Message.Contains("Unhandled exception", StringComparison.Ordinal);
    }

    /// <summary>
    /// One exception entry, or null when the message names no exception type. The top frame is the first in-repo
    /// (<c>Dhadgar.</c>) frame, else the first frame. The stack text is taken from the message only: the log parser keeps no
    /// separate exception field.
    /// </summary>
    private JsonObject? ExceptionEntry(string service, LogLine line)
    {
        Match type = ExceptionTypeName.Match(line.Message);
        if (!type.Success)
        {
            return null;
        }

        string[] lines = line.Message.Split('\n').Select(text => text.Trim()).ToArray();
        string[] frames = lines.Where(text => text.StartsWith("at ", StringComparison.Ordinal)).Select(text => text[3..].Trim()).ToArray();
        string? top = frames.FirstOrDefault(frame => frame.StartsWith("Dhadgar.", StringComparison.Ordinal)) ?? frames.FirstOrDefault();
        string message = Truncate(lines.Length > 0 ? lines[0] : string.Empty);
        string redactedMessage = _redactor.Redact(message, out int messageRedactions);
        _passRedactions += messageRedactions;
        string[] topFrames = [];
        if (top is not null)
        {
            topFrames = [_redactor.Redact(top, out int frameRedactions)];
            _passRedactions += frameRedactions;
        }

        return new JsonObject
        {
            ["service"] = service,
            ["type"] = type.Value,
            ["frames"] = JsonSerializer.SerializeToNode(topFrames, JsonOptions),
            ["message"] = redactedMessage,
            ["at"] = line.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>One 5xx request entry from a Hosting.Diagnostics finish line, or null for any other line.</summary>
    private JsonObject? RequestEntry(string service, LogLine line, IReadOnlyList<UatRowFault> faults)
    {
        if (!line.Category.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics", StringComparison.Ordinal))
        {
            return null;
        }

        Match match = RequestFinished.Match(line.Message);
        if (!match.Success || !int.TryParse(match.Groups["status"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int status)
            || status < 500 || status > 599)
        {
            return null;
        }

        string url = match.Groups["url"].Value;
        string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath : url;
        string route = _redactor.Redact(RouteTemplateOf(path), out int routeRedactions);
        _passRedactions += routeRedactions;
        string[] active = faults
            .Where(fault => fault.InjectedAt <= line.Timestamp && (fault.HealedAt is null || fault.HealedAt >= line.Timestamp))
            .Select(fault => fault.FaultId)
            .ToArray();
        return new JsonObject
        {
            ["service"] = service,
            ["status"] = status,
            ["error_code"] = null,
            ["method"] = match.Groups["method"].Value,
            ["route"] = route,
            ["faults"] = JsonSerializer.SerializeToNode(active, JsonOptions),
            ["at"] = line.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// The route template of a request path: the query is dropped, GUID segments become <c>{id}</c> and whole numeric segments
    /// <c>{n}</c>. A version segment such as <c>v1</c> stays, so the shipped known-issue patterns match the signature.
    /// </summary>
    private static string RouteTemplateOf(string path)
    {
        string route = path;
        int query = route.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            route = route[..query];
        }

        return NumericSegment.Replace(GuidSegment.Replace(route, "{id}"), "{n}");
    }

    private static string Truncate(string text) => text.Length <= EvidenceTextLimit ? text : text[..EvidenceTextLimit];

    private void Write(string relative, string text)
    {
        string path = Path.Combine(Folder!, relative);
        Result<string> checkedPath = _validator.ValidatePath(path, [Folder!]);
        if (checkedPath.IsFailure)
        {
            throw new IOException("bundle path rejected");
        }

        string? dir = Path.GetDirectoryName(checkedPath.Value);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        string redacted = _redactor.Redact(text, out int count);
        _passRedactions += count;
        File.WriteAllText(checkedPath.Value!, redacted, new UTF8Encoding(false));
    }

    private string ReproToml(UatSpec spec, ulong seed, ReplayFidelity fidelity)
    {
        var text = new StringBuilder();
        text.Append("seed = ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("build_id = ").Append(Quote(_buildId)).Append('\n');
        text.Append("section = ").Append(Quote(spec.Section.Id)).Append('\n');
        text.Append("rows = [").Append(string.Join(", ", _results.Select(row => Quote(row.RowId)))).Append("]\n");
        text.Append("label = ").Append(Quote(_label ?? string.Empty)).Append('\n');
        text.Append("fidelity = ").Append(Quote(FidelityText(fidelity))).Append('\n');
        text.Append("fingerprint_sha256 = ").Append(Quote(_fingerprintSha)).Append('\n');
        text.Append("\n[[specs]]\n");
        text.Append("file = ").Append(Quote(spec.File)).Append('\n');
        text.Append("sha256 = ").Append(Quote(spec.Sha256)).Append('\n');
        foreach (UatRowResult row in _results)
        {
            foreach (UatRowFault fault in row.Faults ?? Array.Empty<UatRowFault>())
            {
                DateTimeOffset rowStart = row.StartedAt ?? fault.InjectedAt;
                JsonElement? args = Evidence(fault.Args);
                text.Append("\n[[fault]]\n");
                text.Append("row = ").Append(Quote(row.RowId)).Append('\n');
                text.Append("id = ").Append(Quote(fault.Id)).Append('\n');
                text.Append("tool = ").Append(Quote(fault.Tool)).Append('\n');
                text.Append("args = ").Append(Quote(args?.GetRawText() ?? "{}")).Append('\n');
                text.Append("injected_ms = ").Append(((long)(fault.InjectedAt - rowStart).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append('\n');
                if (fault.HealedAt is { } healed)
                {
                    text.Append("healed_ms = ").Append(((long)(healed - rowStart).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes the value as a TOML basic string. Backslash and quote are escaped; tab, newline and carriage return use their short
    /// escapes; any other control character (below U+0020 or U+007F) becomes <c>\uXXXX</c>.
    /// </summary>
    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        return text.Append('"').ToString();
    }

}
