using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Twins;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>twin_run</c>. <c>experiment</c> is the file name without <c>.toml</c> of a file under <c>twins/</c>.
/// <c>seed</c> overrides the experiment's seed. <c>lease_id</c> is used when it matches the current lease; without it the run takes its own lease.
/// </summary>
public sealed record TwinRunArgs(
    string? Experiment = null,
    string? SpecsDir = null,
    ulong? Seed = null,
    string? Label = null,
    string? LeaseId = null);

/// <summary>
/// <c>twin_run</c> (OwnLease): loads one experiment and its two catalog specs, then runs the base world and the variant world through
/// <see cref="TwinRunner"/>. Both bundles are ingested into the results index at run end (D-ML91). An experiment with problems is refused
/// before any step.
/// </summary>
public sealed class TwinRunTool : LabTool<TwinRunArgs>
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The runner is resolved at call time because the tool registry contains this tool and the runner
    /// depends on the registry's dispatcher.
    /// </summary>
    public TwinRunTool(LabPaths paths, IPathValidator validator, IServiceProvider services)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "twin_run";

    /// <inheritdoc />
    public override string Description => "Runs one paired twin experiment: the base world, then the variant world with its faults, then the relation. Uses the given lease_id, or takes and releases its own. Returns experiment, held (null when incomplete), overall, the twin row, both runs with their bundles, the diffs, and both ingests.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(TwinRunArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(args.Experiment))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] experiment is required");
        }

        if (!TwinSpecLoader.IdPattern.IsMatch(args.Experiment))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the experiment name must match ^[a-z0-9-]{1,64}$");
        }

        Result<string> dir = UatSpecPaths.ResolveDir(args.SpecsDir, _paths, _validator);
        if (dir.IsFailure)
        {
            return Result<object>.Failure(dir.Error);
        }

        string twinsDir = Path.Combine(dir.Value!, "twins");
        if (!Directory.Exists(twinsDir))
        {
            return Result<object>.Failure("[Lab.NotFound] no twins folder");
        }

        Result<string> path = UatSpecPaths.ResolveSpec(twinsDir, args.Experiment + ".toml", _validator);
        if (path.IsFailure)
        {
            return Result<object>.Failure(path.Error);
        }

        Result<string> text = await ReadSpecAsync(path.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<object>.Failure(text.Error);
        }

        Result<TwinSpec> loaded = TwinSpecLoader.Load(text.Value!);
        if (loaded.IsFailure)
        {
            return Result<object>.Failure(loaded.Error);
        }

        if (!string.Equals(loaded.Value!.Id, args.Experiment, StringComparison.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the experiment id must be its file name");
        }

        Result<UatSpec> baseSpec = await LoadCatalogSpecAsync(dir.Value!, loaded.Value!.Base.Spec, ct).ConfigureAwait(false);
        if (baseSpec.IsFailure)
        {
            return Result<object>.Failure(baseSpec.Error);
        }

        Result<UatSpec> variantSpec = await LoadCatalogSpecAsync(dir.Value!, loaded.Value!.Variant.Spec, ct).ConfigureAwait(false);
        if (variantSpec.IsFailure)
        {
            return Result<object>.Failure(variantSpec.Error);
        }

        string? label = string.IsNullOrEmpty(args.Label) ? null : args.Label;
        var baseWriter = NewSink(label);
        var variantWriter = NewSink(label);
        using HttpMessageHandler probeHandler = LabHttp.CreateHandler();
        var live = await LiveFingerprint.CollectAsync(
            _services.GetRequiredService<IDockerCli>(), DockerEngine.Linux, Array.Empty<string>(), probeHandler, ct).ConfigureAwait(false);
        baseWriter.UseLiveParts(live);
        variantWriter.UseLiveParts(live);

        Result<TwinRunResult> run = await _services.GetRequiredService<TwinRunner>().RunAsync(
            loaded.Value!, baseSpec.Value!, variantSpec.Value!, baseWriter, variantWriter, args.Seed, args.LeaseId, label, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<object>.Failure(run.Error);
        }

        if (baseWriter.WriteError is not null || variantWriter.WriteError is not null)
        {
            return Result<object>.Failure(baseWriter.WriteError ?? variantWriter.WriteError!);
        }

        return Result<object>.Success(await PayloadAsync(run.Value!, baseWriter, variantWriter, ct).ConfigureAwait(false));
    }

    /// <summary>Builds the bundle writer of one world, with the build, the logs and the live parts of the run.</summary>
    private BundleWriter NewSink(string? label)
    {
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
        sink.UseBuild(LastBuildReader.Read(_paths, _validator, _services.GetService<RunningMutantBuild>()));
        IRunLogSource? logs = _services.GetService<IRunLogSource>();
        if (logs is not null)
        {
            sink.UseLogs(logs);
        }

        return sink;
    }

    /// <summary>Reads one spec file, refusing one larger than the spec size limit or one that cannot be read.</summary>
    private static async Task<Result<string>> ReadSpecAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return Result<string>.Failure("[Lab.NotFound] no such spec");
        }

        if (new FileInfo(path).Length > UatSpecPaths.MaxSpecBytes)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the spec is larger than 1 MiB");
        }

        try
        {
            return Result<string>.Success(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.Unavailable] the spec could not be read");
        }
    }

    /// <summary>Loads and validates one catalog spec by file name; a spec with validation problems is refused.</summary>
    private async Task<Result<UatSpec>> LoadCatalogSpecAsync(string specsDir, string name, CancellationToken ct)
    {
        Result<string> path = UatSpecPaths.ResolveSpec(specsDir, name, _validator);
        if (path.IsFailure)
        {
            return Result<UatSpec>.Failure(path.Error);
        }

        Result<string> text = await ReadSpecAsync(path.Value!, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<UatSpec>.Failure(text.Error);
        }

        SpecLoadOutcome loaded = SpecLoader.LoadText(name, text.Value!);
        if (loaded.Spec is null)
        {
            return Result<UatSpec>.Failure("[Lab.InvalidArgument] the spec does not load: " + loaded.Problems.Count + " problem(s)");
        }

        LabToolRegistry registry = _services.GetRequiredService<LabToolRegistry>();
        if (SpecValidator.Validate(loaded.Spec, text.Value!, registry).Count > 0)
        {
            return Result<UatSpec>.Failure("[Lab.InvalidArgument] the spec has validation problems; run uat_plan for the list");
        }

        return Result<UatSpec>.Success(loaded.Spec);
    }

    /// <summary>
    /// The result: the relation, both runs with their bundle folders and ingests (D-ML91; an ingest failure does not fail the run), and the diffs
    /// as <c>{pointer, base, variant}</c>.
    /// </summary>
    private async Task<JsonObject> PayloadAsync(TwinRunResult run, BundleWriter baseWriter, BundleWriter variantWriter, CancellationToken ct)
    {
        ResultsService results = _services.GetRequiredService<ResultsService>();
        JsonObject? baseIngest = await UatRunTool.IngestAfterRunAsync(results, baseWriter.Folder, ct).ConfigureAwait(false);
        JsonObject? variantIngest = await UatRunTool.IngestAfterRunAsync(results, variantWriter.Folder, ct).ConfigureAwait(false);

        var diffs = new JsonArray();
        foreach (TwinDiff diff in run.Diffs)
        {
            diffs.Add(new JsonObject
            {
                ["pointer"] = diff.Locator,
                ["base"] = diff.Base is { } b ? JsonNode.Parse(b.GetRawText()) : null,
                ["variant"] = diff.Variant is { } v ? JsonNode.Parse(v.GetRawText()) : null,
            });
        }

        return new JsonObject
        {
            ["experiment"] = run.ExperimentId,
            ["held"] = run.Held is { } held ? JsonValue.Create(held) : null,
            ["overall"] = JsonValue.Create(OverallText(run.Overall)),
            ["twin_row"] = new JsonObject
            {
                ["id"] = run.TwinRow.RowId,
                ["grade"] = run.TwinRow.Grade.ToString().ToLowerInvariant(),
                ["reason"] = run.TwinRow.Reason,
            },
            ["base"] = new JsonObject { ["run_id"] = run.Base.RunId, ["bundle"] = Path.GetFileName(baseWriter.Folder), ["ingest"] = baseIngest },
            ["variant"] = new JsonObject { ["run_id"] = run.Variant.RunId, ["bundle"] = Path.GetFileName(variantWriter.Folder), ["ingest"] = variantIngest },
            ["diffs"] = diffs,
        };
    }

    private static string OverallText(RunOverall overall) => new RunSummary(overall, Array.Empty<string>(), Array.Empty<string>()).OverallText;
}
