using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
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
