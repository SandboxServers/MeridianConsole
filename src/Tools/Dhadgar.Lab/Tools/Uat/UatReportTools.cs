using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>uat_report</c>: the run id returned by <c>uat_run</c>.
/// </summary>
public sealed record UatReportArgs(string RunId);

/// <summary>
/// Arguments of <c>bundle_read</c>: the run id and a file path inside that run's bundle folder.
/// </summary>
public sealed record BundleReadArgs(string RunId, string Path);

/// <summary>
/// Arguments of <c>uat_attest</c>: the run, the row (section and row id), the human clause, and the verdict.
/// <c>Verdict</c> is <c>held</c> or <c>not_held</c>; <c>pass</c> and <c>fail</c> are refused.
/// </summary>
public sealed record UatAttestArgs(string? RunId, string? Section, string? Row, string? ClauseId, string? Verdict, string? Note = null);

/// <summary>
/// <c>uat_report</c> (Open): returns the <c>run.json</c> of one run with its bundle folder name. Reads only; the grades are
/// the ones the run wrote.
/// </summary>
public sealed class UatReportTool : LabTool<UatReportArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    public UatReportTool(LabPaths paths, IPathValidator validator)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public override string Name => "uat_report";

    /// <inheritdoc />
    public override string Description => "Returns run.json of one run: overall, seed, summary, new_failures, secret_scan, and its bundle folder.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(UatReportArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, args.RunId, _validator);
        if (folder.IsFailure)
        {
            return Result<object>.Failure(folder.Error);
        }

        // An interrupted attestation is completed or rolled back before the bundle is read (OD-1).
        Result recovered = BundleStaging.Recover(folder.Value!, _validator);
        if (recovered.IsFailure)
        {
            return Result<object>.Failure(recovered.Error);
        }

        Result<string> file = BundleFolders.ResolveFile(folder.Value!, "run.json", _validator);
        if (file.IsFailure)
        {
            return Result<object>.Failure(file.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(file.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<object>.Failure(text.Error);
        }

        JsonNode? run = JsonNode.Parse(text.Value!);
        return Result<object>.Success(new { run_id = args.RunId, folder = folder.Value, run });
    }
}

/// <summary>
/// <c>bundle_read</c> (Open): returns one text file of a run's bundle. The path must stay inside that run's folder, so
/// another run's <c>run.json</c> is refused. Output passes through <see cref="Redactor"/> again before it is returned.
/// </summary>
public sealed class BundleReadTool : LabTool<BundleReadArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    public BundleReadTool(LabPaths paths, IPathValidator validator, Redactor redactor)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    /// <inheritdoc />
    public override string Name => "bundle_read";

    /// <inheritdoc />
    public override string Description => "Reads one text file of a run's bundle (for example ledger.md or rows/<section>/<row>.json). path must stay inside the run's folder.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(BundleReadArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, args.RunId, _validator);
        if (folder.IsFailure)
        {
            return Result<object>.Failure(folder.Error);
        }

        // An interrupted attestation is completed or rolled back before the bundle is read (OD-1).
        Result recovered = BundleStaging.Recover(folder.Value!, _validator);
        if (recovered.IsFailure)
        {
            return Result<object>.Failure(recovered.Error);
        }

        Result<string> file = BundleFolders.ResolveFile(folder.Value!, args.Path, _validator);
        if (file.IsFailure)
        {
            return Result<object>.Failure(file.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(file.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<object>.Failure(text.Error);
        }

        return Result<object>.Success(new { run_id = args.RunId, path = args.Path, text = _redactor.Redact(text.Value!) });
    }
}

/// <summary>
/// <c>uat_attest</c> (Leased): records a human verdict on one pending human clause of a row, then regrades the row, rewrites
/// <c>ledger.md</c> and the run's overall. Refuses every non-human clause with <c>[Lab.InvalidArgument]</c>.
/// </summary>
public sealed class UatAttestTool : LabTool<UatAttestArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;
    private readonly LeaseBook _leases;
    private readonly SecretVault _vault;
    private readonly ResultsService _results;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    public UatAttestTool(LabPaths paths, IPathValidator validator, Redactor redactor, TimeProvider time, LeaseBook leases, SecretVault vault,
        ResultsService results)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <inheritdoc />
    public override string Name => "uat_attest";

    /// <inheritdoc />
    public override string Description => "Records the user's held or not_held verdict on one pending human clause of a NEEDS_HUMAN or UNVERIFIED row, then regrades the row and rewrites the ledger and overall, and re-ingests the bundle into the results index (the ingest field; a failed ingest does not undo the attest). Ask the user before each call; the harness prompts only once owner action O2 is applied.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(UatAttestArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<string> folder = BundleFolders.Find(_paths.UatRuns, args.RunId, _validator);
        if (folder.IsFailure)
        {
            return Result<object>.Failure(folder.Error);
        }

        // The dispatcher has already checked the lease; a failure here is returned unchanged.
        Result<LeaseInfo> touched = _leases.Touch(context.LeaseId!);
        if (touched.IsFailure)
        {
            return Result<object>.Failure(touched.Error);
        }

        Result<string> grade = BundleAttestor.Attest(folder.Value!, args.RunId!, args.Section!, args.Row!, args.ClauseId!,
            args.Verdict!, args.Note, _time.GetUtcNow(), new AttestedBy(touched.Value!.Owner, context.Caller), _redactor, _validator,
            new SecretScanner(_vault), _results.LoadKnownIssues());
        if (grade.IsFailure)
        {
            return Result<object>.Failure(grade.Error);
        }

        // D-ML91: the attest has committed through BundleStaging, so a failed re-ingest is reported in ingest and never rolls it back.
        Result<IngestResult> ingested = await _results.IngestAsync(folder.Value!, ct).ConfigureAwait(false);
        return Result<object>.Success(new
        {
            run_id = args.RunId,
            section = args.Section,
            row = args.Row,
            clause_id = args.ClauseId,
            grade = grade.Value,
            ingest = _results.ToPayload(ingested),
        });
    }
}
