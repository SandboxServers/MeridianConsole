using System.Globalization;
using System.Text;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Triage;

/// <summary>
/// The triage observer (L26, D-ML91). After each ingest it opens a finding for every new signature of the run that has none
/// (the bucket id is the signature id), then rewrites <c>triage/ledger.md</c> under <see cref="LabPaths.Triage"/>. It reads the
/// index through <see cref="ResultsService"/> and never calls GitHub. An observer failure is logged by type only and never fails
/// the ingest (A3).
/// </summary>
public sealed class TriageService : IIngestObserver
{
    /// <summary>The ledger file name under <see cref="LabPaths.Triage"/>.</summary>
    public const string LedgerFile = "ledger.md";

    private const string ReproFile = "repro/repro.toml";

    private readonly ResultsService _results;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly ILogger<TriageService> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the observer.
    /// </summary>
    /// <param name="results">The results service that owns the index and its gate.</param>
    /// <param name="paths">The lab layout (the triage folder).</param>
    /// <param name="validator">Validates every path the observer reads or writes.</param>
    /// <param name="logger">The logger; it records the observer's outcome by fixed text only.</param>
    /// <param name="time">The clock for finding creation times.</param>
    public TriageService(ResultsService results, LabPaths paths, IPathValidator validator, ILogger<TriageService> logger,
        TimeProvider time)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public async Task OnIngestedAsync(string runId, CancellationToken ct)
    {
        string now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        Result<IReadOnlyList<ResultsDb.BucketRecord>> ledger =
            await _results.ReadAsync(db => OpenFindings(db, runId, now), ct).ConfigureAwait(false);
        if (ledger.IsFailure)
        {
            // The error text can name a path or a SQLite detail; the log records the fixed outcome only.
            _logger.LogWarning("Triage ledger skipped for an ingested run");
            return;
        }

        Result written = await WriteLedgerAsync(ledger.Value, ct).ConfigureAwait(false);
        if (written.IsFailure)
        {
            _logger.LogWarning("Triage ledger could not be written");
        }
    }

    /// <summary>
    /// Opens a finding for each new, unfounded signature of the run, then reads every bucket. Runs under the results gate.
    /// </summary>
    internal Result<IReadOnlyList<ResultsDb.BucketRecord>> OpenFindings(ResultsDb db, string runId, string createdAt)
    {
        Result<RunRecord> run = db.RunOf(runId);
        if (run.IsFailure)
        {
            return Result<IReadOnlyList<ResultsDb.BucketRecord>>.Failure(run.Error);
        }

        string? repro = null;
        Result<string> reproPath = BundleFolders.ResolveFile(run.Value.BundlePath, ReproFile, _validator);
        if (reproPath.IsSuccess)
        {
            repro = reproPath.Value;
        }

        Result<IReadOnlyList<string>> signatures = db.UnfoundedNewSignaturesOfRun(runId);
        if (signatures.IsFailure)
        {
            return Result<IReadOnlyList<ResultsDb.BucketRecord>>.Failure(signatures.Error);
        }

        foreach (string signatureId in signatures.Value)
        {
            Result opened = db.InsertFinding(signatureId, runId, repro, createdAt);
            if (opened.IsFailure)
            {
                return Result<IReadOnlyList<ResultsDb.BucketRecord>>.Failure(opened.Error);
            }
        }

        return db.LedgerRows();
    }

    /// <summary>
    /// Renders the ledger as a Markdown table, one row per bucket. A cell holds no raw pipe or line break.
    /// </summary>
    /// <param name="buckets">The buckets, in signature id order.</param>
    /// <param name="generatedAt">The render time, as text.</param>
    public static string RenderLedger(IReadOnlyList<ResultsDb.BucketRecord> buckets, string generatedAt)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        var text = new StringBuilder();
        text.Append("# Triage ledger\n\n");
        text.Append("Generated ").Append(Cell(generatedAt)).Append(". One row per signature bucket; a finding is open for each new bucket.\n\n");
        text.Append("| id | kind | state | hits | first seen | last seen | known issue | repro | draft |\n");
        text.Append("| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (ResultsDb.BucketRecord bucket in buckets)
        {
            text.Append("| ").Append(string.Join(" | ",
                Cell(bucket.SignatureId), Cell(bucket.Kind), Cell(bucket.State),
                bucket.Hits.ToString(CultureInfo.InvariantCulture), Cell(bucket.FirstSeen), Cell(bucket.LastSeen),
                Cell(bucket.KnownIssueId), Cell(bucket.ReproPath), Cell(bucket.DraftPath)));
            text.Append(" |\n");
        }

        return text.ToString();
    }

    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private async Task<Result> WriteLedgerAsync(IReadOnlyList<ResultsDb.BucketRecord> buckets, CancellationToken ct)
    {
        Result<string> path = _validator.ValidatePath(Path.Combine(_paths.Triage, LedgerFile), [_paths.Triage]);
        if (path.IsFailure)
        {
            return Result.Failure("[Lab.InvalidArgument] the ledger path is outside the triage folder");
        }

        try
        {
            Directory.CreateDirectory(_paths.Triage);
            string text = RenderLedger(buckets, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            await File.WriteAllTextAsync(path.Value, text, Encoding.UTF8, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.IoError] the ledger could not be written: " + ex.GetType().Name);
        }
    }
}
