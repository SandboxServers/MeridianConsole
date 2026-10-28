using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Observe;
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
    private readonly string _buildId;
    private readonly string _system;
    private readonly string _build;
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
    }

    /// <summary>
    /// The bundle folder once the run finished, or null for a plan-only run or a write failure.
    /// </summary>
    public string? Folder { get; private set; }

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
            Commit: FingerprintBuilder.UnknownPart,
            Dirty: false,
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
        _warnings.Add("commit not collected: unknown");
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
        TimingFaults: false,
        Truncated: truncated,
        Attested: false,
        BuildPresent: _buildId != "nobuild"));

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
        var clauses = (spec?.Expect ?? Array.Empty<UatClause>()).Select(clause =>
        {
            UatClauseResult? result = row.Clauses.FirstOrDefault(candidate => candidate.Id == clause.Id);
            return new LedgerClause(clause.Id, clause.Pointer ?? clause.Source, result?.State ?? ClauseState.Unverified, result?.Error, clause.Source, clause.Required);
        }).ToList();
        _rows.Add(new LedgerRow(section, row.RowId, spec?.Title ?? row.RowId, spec?.Expected ?? string.Empty, row.Grade, _time.GetUtcNow(), clauses));
    }

    /// <inheritdoc />
    public void RunFinished(UatRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.PlanOnly || _spec is null)
        {
            return;
        }

        string name = $"{_started.UtcDateTime:yyyyMMdd}-{_buildId}-{_runId}";
        string folder = Path.Combine(_uatRuns, name);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteError = "[Lab.Unavailable] the bundle could not be written";
        }
    }

    private void WriteAll(UatRunResult result, bool secretFailed, IReadOnlyList<string> removed)
    {
        _passRedactions = 0;
        UatSpec spec = _spec!;
        IReadOnlyList<LedgerRow> rows = secretFailed
            ? _rows.Select(row => row with { Grade = RowGrade.Blocked, Title = row.Row, Expected = string.Empty, Clauses = Array.Empty<LedgerClause>() }).ToList()
            : _rows;
        string overall = secretFailed ? "fail" : OverallText(result.Overall);
        string secretScan = secretFailed ? "failed" : "passed";

        var summary = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["PASS"] = 0, ["FAIL"] = 0, ["BLOCKED"] = 0, ["SKIPPED"] = 0, ["NEEDS_HUMAN"] = 0, ["UNVERIFIED"] = 0,
        };
        foreach (LedgerRow row in rows)
        {
            summary[LedgerRenderer.ResultText(row.Grade)]++;
        }

        string[] failedRows = rows.Where(row => row.Grade is RowGrade.Fail or RowGrade.Blocked or RowGrade.Unverified)
            .Select(row => row.Section + "/" + row.Row).ToArray();

        if (_fingerprint is not null && !secretFailed)
        {
            Write("fingerprint.json", JsonSerializer.Serialize(_fingerprint, JsonOptions));
        }

        foreach (LedgerRow row in rows)
        {
            UatRowResult? source = _results.FirstOrDefault(candidate => candidate.RowId == row.Row);
            var clauseJson = row.Clauses.Select(clause =>
            {
                UatClauseResult? evidence = source?.Clauses.FirstOrDefault(candidate => candidate.Id == clause.Id);
                return new
                {
                    id = clause.Id,
                    pointer = clause.Locator,
                    source = clause.Source,
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
                    stepJson.Add(new
                    {
                        id = step.Id,
                        tool = step.Tool,
                        ok = step.Ok,
                        error = step.Error,
                        error_code = step.ErrorCode,
                        expected_error = step.ExpectedError,
                        duration_ms = step.DurationMs,
                        result = Evidence(step.Result),
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
                at = row.At,
                clauses = secretFailed ? Array.Empty<object>() : clauseJson.Cast<object>(),
                steps = stepJson,
                attestations = Array.Empty<object>(),
            };
            Write(Path.Combine("rows", row.Section, row.Row + ".json"), JsonSerializer.Serialize(rowJson, JsonOptions));
        }

        Write(Path.Combine("repro", "repro.toml"), ReproToml(spec, result.Seed, CurrentFidelity(result.Truncated)));
        Write("ledger.md", LedgerRenderer.Render(_runId, _system, _build, rows));

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
            build = new { id = _buildId, commit = string.Empty, dirty = false, checkout = string.Empty },
            lease_owner = (string?)null,
            members = Array.Empty<string>(),
            specs = new[] { new { file = spec.File, sha256 = spec.Sha256 } },
            tools = Array.Empty<string>(),
            summary,
            overall,
            incomplete_reasons = Array.Empty<string>(),
            new_failures = failedRows,
            known_failures = Array.Empty<string>(),
            violations = 0,
            redactions = _passRedactions,
            secret_scan = secretScan,
            secret_scan_removed = removed,
            fingerprint_sha256 = _fingerprintSha,
            fidelity = FidelityText(CurrentFidelity(result.Truncated)),
            pair = (object?)null,
            replay = (object?)null,
            events = result.Events,
            warnings = _warnings.ToArray(),
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

        _redactor.Redact(value.GetRawText(), out int count);
        _passRedactions += count;
        JsonElement redacted = _redactor.Redact(value);
        string text = redacted.GetRawText();
        return text.Length > MaxEvidenceChars
            ? JsonSerializer.SerializeToElement(new { truncated = true, text = text[..MaxEvidenceChars] })
            : redacted;
    }

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
        return text.ToString();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string OverallText(RunOverall overall) => overall switch
    {
        RunOverall.Pass => "pass",
        RunOverall.PassWithKnown => "pass_with_known",
        RunOverall.Incomplete => "incomplete",
        RunOverall.Fail => "fail",
        _ => throw new ArgumentOutOfRangeException(nameof(overall)),
    };
}
