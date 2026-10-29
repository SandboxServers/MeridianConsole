using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Coverage;

/// <summary>
/// Records one run's behavioural coverage (L52, C12 <c>coverage</c>) after it is ingested. It implements
/// <see cref="IIngestObserver"/>; <see cref="BundleIngestor"/> calls it after the run is in the index and never edits the
/// ingestor. Only <c>invariant_check</c> is recorded at ingest (<c>invariants.json</c>). The route, fault, error code and transition
/// kinds are not collected until L52b (D-ML99); <c>coverage_report</c> flags them as not collected, and no route coverage is claimed.
/// </summary>
public sealed class CoverageCollector : IIngestObserver
{
    /// <summary>The bundle file that lists the invariant violations of a run (C9).</summary>
    public const string InvariantsFile = "invariants.json";

    private readonly ResultsService _results;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the collector.
    /// </summary>
    /// <param name="results">The results service; reads and writes run under its gate.</param>
    /// <param name="validator">Validates the bundle paths.</param>
    public CoverageCollector(ResultsService results, IPathValidator validator)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failure throws, so <see cref="BundleIngestor"/> logs the exception type and the ingest still succeeds (A3).
    /// </remarks>
    public async Task OnIngestedAsync(string runId, CancellationToken ct)
    {
        Result<RunRecord> run = await _results.ReadAsync(db => db.RunOf(runId), ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            throw new InvalidOperationException(run.Error);
        }

        Result<IReadOnlyList<CoverageRecord>> hits = await HitsOfBundleAsync(runId, run.Value.BundlePath, ct).ConfigureAwait(false);
        if (hits.IsFailure)
        {
            throw new InvalidOperationException(hits.Error);
        }

        Result written = await _results.WriteAsync(db => db.ReplaceCoverage(runId, hits.Value), ct).ConfigureAwait(false);
        if (written.IsFailure)
        {
            throw new InvalidOperationException(written.Error);
        }
    }

    /// <summary>
    /// The hits of a bundle's <c>invariants.json</c>: each violation is an <c>invariant_check</c> hit, because the check ran.
    /// A bundle without the file has no invariant hits.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="bundleFolder">The bundle folder, validated before it is read.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<IReadOnlyList<CoverageRecord>>> HitsOfBundleAsync(string runId, string bundleFolder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bundleFolder);
        string candidate = Path.Combine(bundleFolder, InvariantsFile);
        if (!File.Exists(candidate))
        {
            return Result<IReadOnlyList<CoverageRecord>>.Success(CoverageKeys.Count(runId, []));
        }

        Result<string> path = BundleFolders.ResolveFile(bundleFolder, InvariantsFile, _validator);
        if (path.IsFailure)
        {
            return Result<IReadOnlyList<CoverageRecord>>.Failure(path.Error);
        }

        Result<string> text = await BundleFolders.ReadTextAsync(path.Value, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return Result<IReadOnlyList<CoverageRecord>>.Failure(text.Error);
        }

        return HitsOfInvariants(runId, text.Value);
    }

    /// <summary>
    /// The <c>invariant_check</c> hits of an <c>invariants.json</c> text. A malformed file is a failure, never a throw.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="json">The file text.</param>
    public static Result<IReadOnlyList<CoverageRecord>> HitsOfInvariants(string runId, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<CoverageRecord>>.Failure("[Lab.InvalidArgument] invariants.json is not valid JSON");
        }

        var hits = new List<(string Kind, string Key)>();
        if (root?["violations"] is JsonArray violations)
        {
            foreach (JsonObject violation in violations.OfType<JsonObject>())
            {
                string? invariantId = TextOf(violation, "invariant_id");
                string? checkId = TextOf(violation, "check_id");
                if (!string.IsNullOrEmpty(invariantId) && !string.IsNullOrEmpty(checkId))
                {
                    hits.Add((CoverageKeys.InvariantCheck, CoverageKeys.InvariantKey(invariantId, checkId)));
                }
            }
        }

        return Result<IReadOnlyList<CoverageRecord>>.Success(CoverageKeys.Count(runId, hits));
    }

    private static string? TextOf(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
