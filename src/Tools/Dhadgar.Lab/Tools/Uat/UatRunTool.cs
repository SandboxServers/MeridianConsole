using System.Net.Http;
using System.Runtime.InteropServices;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>uat_run</c>. <c>spec</c> is one file name or <c>catalog/&lt;file&gt;</c>. <c>sections</c> and <c>rows</c>
/// filter the run. <c>lease_id</c> is used when it matches the current lease; without it the run takes its own lease.
/// <c>replay</c> is a bundle-relative <c>&lt;run_id&gt;\repro\repro.toml</c>: the seed, section, rows and spec come from that file,
/// and a spec whose sha256 differs from the recorded one is refused.
/// </summary>
public sealed record UatRunArgs(
    string? Spec = null,
    string? SpecsDir = null,
    IReadOnlyList<string>? Sections = null,
    IReadOnlyList<string>? Rows = null,
    bool PlanOnly = false,
    string? Label = null,
    ulong? Seed = null,
    string? Replay = null,
    string? LeaseId = null);

/// <summary>
/// <c>uat_run</c> (OwnLease): loads and validates one spec, then runs it through <see cref="UatRunner"/>. A spec with
/// problems is refused before any step. <c>plan_only</c> validates and returns without a lease.
/// </summary>
public sealed class UatRunTool : LabTool<UatRunArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The runner is resolved at call time because the tool registry contains this tool and the runner
    /// depends on the registry's dispatcher.
    /// </summary>
    public UatRunTool(LabPaths paths, IPathValidator validator, IServiceProvider services)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "uat_run";

    /// <inheritdoc />
    public override string Description => "Runs one UAT spec (or plans it with plan_only). Uses the given lease_id, or takes and releases its own. Returns run_id, seed, overall, the rows, bundle, and ingest (the results index outcome; null for plan_only).";

    /// <summary>
    /// Reads <c>repro\repro.toml</c> of one run. The first path segment is the run id; the rest must be a <c>repro</c> file
    /// inside that bundle, which <see cref="BundleFolders.ResolveFile"/> checks against the bundle folder.
    /// </summary>
    private async Task<Result<ReproSettings>> LoadReproAsync(string replay, CancellationToken ct)
    {
        string[] parts = replay.Split(['\\', '/'], 2);
        bool inReproFolder = parts.Length == 2 &&
            (parts[1].StartsWith("repro\\", StringComparison.Ordinal) || parts[1].StartsWith("repro/", StringComparison.Ordinal));
        if (!inReproFolder || !parts[1].EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ReproSettings>.Failure("[Lab.InvalidArgument] replay must be <run_id>\\repro\\<file>.toml");
        }

        Result<string> folder = BundleFolders.Find(_paths.UatRuns, parts[0], _validator);
        if (folder.IsFailure)
        {
            return Result<ReproSettings>.Failure(folder.Error);
        }

        Result<string> file = BundleFolders.ResolveFile(folder.Value!, parts[1], _validator);
        if (file.IsFailure)
        {
            return Result<ReproSettings>.Failure(file.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(file.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<ReproSettings>.Failure(text.Error);
        }

        return ReproReader.Parse(text.Value!);
    }

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(UatRunArgs args, LabCallContext context, CancellationToken ct)
    {
        bool hasSpec = !string.IsNullOrEmpty(args.Spec);
        bool hasReplay = !string.IsNullOrEmpty(args.Replay);
        if (!hasSpec && !hasReplay)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] spec or replay is required");
        }

        if (hasSpec && hasReplay)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] give spec or replay, not both");
        }

        ReproSettings? repro = null;
        if (hasReplay)
        {
            Result<ReproSettings> loadedRepro = await LoadReproAsync(args.Replay!, ct).ConfigureAwait(false);
            if (loadedRepro.IsFailure)
            {
                return Result<object>.Failure(loadedRepro.Error);
            }

            repro = loadedRepro.Value!;
        }

        string specName = repro?.SpecFile ?? args.Spec!;
        IReadOnlyList<string>? sectionFilter = repro is null ? args.Sections : [repro.Section];
        IReadOnlyList<string>? rowFilter = repro is null ? args.Rows : repro.Rows;

        Result<string> dir = UatSpecPaths.ResolveDir(args.SpecsDir, _paths, _validator);
        if (dir.IsFailure)
        {
            return Result<object>.Failure(dir.Error);
        }

        Result<string> path = UatSpecPaths.ResolveSpec(dir.Value!, specName, _validator);
        if (path.IsFailure)
        {
            return Result<object>.Failure(path.Error);
        }

        if (!File.Exists(path.Value!))
        {
            return Result<object>.Failure("[Lab.NotFound] no such spec");
        }

        if (new FileInfo(path.Value!).Length > UatSpecPaths.MaxSpecBytes)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the spec is larger than 1 MiB");
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path.Value!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<object>.Failure("[Lab.Unavailable] the spec could not be read");
        }

        SpecLoadOutcome loaded = SpecLoader.LoadText(specName, text);
        if (loaded.Spec is null)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the spec does not load: " + loaded.Problems.Count + " problem(s)");
        }

        LabToolRegistry registry = _services.GetRequiredService<LabToolRegistry>();
        if (SpecValidator.Validate(loaded.Spec, text, registry).Count > 0)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the spec has validation problems; run uat_plan for the list");
        }

        if (repro is not null && loaded.Spec.Sha256 != repro.SpecSha256)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] spec changed since the run");
        }

        if (sectionFilter is { Count: > 0 } && !sectionFilter.Contains(loaded.Spec.Section.Id, StringComparer.Ordinal))
        {
            return Result<object>.Failure("[Lab.NotFound] the section is not in the spec");
        }

        string? label = string.IsNullOrEmpty(args.Label) ? repro?.Label : args.Label;
        var request = new UatRunRequest(
            loaded.Spec,
            rowFilter is { Count: > 0 } ? rowFilter : null,
            repro is null ? args.Seed : repro.Seed,
            args.LeaseId,
            label,
            args.PlanOnly);

        UatRunner runner = _services.GetRequiredService<UatRunner>();
        var sink = new BundleWriter(
            _paths.UatRuns,
            _validator,
            _services.GetRequiredService<Redactor>(),
            new SecretScanner(_services.GetRequiredService<SecretVault>()),
            _services.GetRequiredService<TimeProvider>(),
            buildId: "nobuild",
            system: RuntimeInformation.OSDescription,
            build: string.Empty,
            label: label);
        sink.UseBuild(LastBuildReader.Read(_paths, _validator));
        IRunLogSource? logs = _services.GetService<IRunLogSource>();
        if (logs is not null)
        {
            sink.UseLogs(logs);
        }

        using HttpMessageHandler probeHandler = LabHttp.CreateHandler();
        sink.UseLiveParts(await LiveFingerprint.CollectAsync(
            _services.GetRequiredService<IDockerCli>(), DockerEngine.Linux, Array.Empty<string>(), probeHandler, ct).ConfigureAwait(false));

        Result<UatRunResult> run = await runner.RunAsync(request, sink, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<object>.Failure(run.Error);
        }

        if (sink.WriteError is not null)
        {
            return Result<object>.Failure(sink.WriteError);
        }

        // D-ML91: the written bundle is ingested into the results index at run end. An ingest failure does not fail the run.
        System.Text.Json.Nodes.JsonObject? ingest = await IngestAfterRunAsync(
            _services.GetRequiredService<ResultsService>(), sink.Folder, ct).ConfigureAwait(false);

        // C4: the result carries the bundle folder name, or null when no bundle was written (plan_only).
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(run.Value!, LabJson.Options) as System.Text.Json.Nodes.JsonObject
            ?? new System.Text.Json.Nodes.JsonObject();
        payload["bundle"] = sink.Folder is { } folder ? Path.GetFileName(folder) : null;
        payload["ingest"] = ingest;

        // A4: the overall is the one the bundle wrote to run.json (D-ML89), whenever the sink wrote one.
        // The payload's overall is the bundle's; ingest.overall is the results index's (D-ML92).
        if (sink.FinalOverall is { } finalOverall)
        {
            payload["overall"] = finalOverall;
        }

        return Result<object>.Success(payload);
    }

    /// <summary>
    /// Ingests the bundle folder written by a run into the results index (D-ML91). Returns null when no bundle was written
    /// (plan_only); otherwise the ingest payload, which is <c>{"ok":false,"error"}</c> on a failed ingest.
    /// </summary>
    /// <param name="results">The results service that owns the database.</param>
    /// <param name="folder">The bundle folder, or null when no bundle was written.</param>
    /// <param name="ct">The cancellation token.</param>
    internal static async Task<System.Text.Json.Nodes.JsonObject?> IngestAfterRunAsync(ResultsService results, string? folder,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (folder is null)
        {
            return null;
        }

        return results.ToPayload(await results.IngestAsync(folder, ct).ConfigureAwait(false));
    }
}
