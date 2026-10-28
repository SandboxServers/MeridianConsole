using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
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
    }

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
            WriteAll(result, secretFailed: false);
            SecretScanResult scan = _scanner.ScanFolder(Folder);
            if (!scan.Passed)
            {
                WriteAll(result, secretFailed: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteError = "[Lab.Unavailable] the bundle could not be written";
        }
    }

    private void WriteAll(UatRunResult result, bool secretFailed)
    {
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
            redactions = 0,
            secret_scan = secretScan,
            fingerprint_sha256 = string.Empty,
            fidelity = "evidence_only",
            pair = (object?)null,
            replay = (object?)null,
            events = 0,
        };

        Write("run.json", JsonSerializer.Serialize(run, JsonOptions));
        foreach (LedgerRow row in rows)
        {
            var clauseJson = row.Clauses.Select(clause => new { id = clause.Id, pointer = clause.Locator, source = clause.Source, required = clause.Required, state = clause.State.ToString(), error = clause.Error });
            string reason = secretFailed ? SecretScanFailed : _results.FirstOrDefault(candidate => candidate.RowId == row.Row)?.Reason ?? string.Empty;
            var rowJson = new { section = row.Section, id = row.Row, title = row.Title, expected = row.Expected, grade = LedgerRenderer.ResultText(row.Grade), reason, at = row.At, clauses = clauseJson };
            Write(Path.Combine("rows", row.Section, row.Row + ".json"), JsonSerializer.Serialize(rowJson, JsonOptions));
        }

        Write(Path.Combine("repro", "repro.toml"), ReproToml(spec, result.Seed));
        Write("ledger.md", LedgerRenderer.Render(_runId, _system, _build, rows));
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

        File.WriteAllText(checkedPath.Value!, _redactor.Redact(text), new UTF8Encoding(false));
    }

    private string ReproToml(UatSpec spec, ulong seed)
    {
        var text = new StringBuilder();
        text.Append("seed = ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("build_id = ").Append(Quote(_buildId)).Append('\n');
        text.Append("section = ").Append(Quote(spec.Section.Id)).Append('\n');
        text.Append("rows = [").Append(string.Join(", ", _results.Select(row => Quote(row.RowId)))).Append("]\n");
        text.Append("label = ").Append(Quote(_label ?? string.Empty)).Append('\n');
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
