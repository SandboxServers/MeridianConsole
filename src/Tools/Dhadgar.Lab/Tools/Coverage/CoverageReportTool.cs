using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Coverage;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>coverage_report</c>: the first run of the window (default: all runs) and one kind (default: all kinds).
/// </summary>
public sealed record CoverageReportArgs(string? SinceRunId = null, string? Kind = null);

/// <summary>
/// <c>coverage_report</c> (Open, L52): totals, hits and unhit keys per kind over the runs since <c>since_run_id</c>. Unhit is
/// the universe minus the hits. Keys that are outside the universe (a 5xx status, or a path that matched no template) are
/// listed under <c>outside_universe</c> and never count as hits. Reads go through <see cref="ResultsService"/>; the tool takes
/// no path and never writes.
/// </summary>
public sealed class CoverageReportTool : LabTool<CoverageReportArgs>
{
    private static readonly string[] UniverseStatusClasses = ["2xx", "4xx"];

    /// <summary>
    /// The kinds that no ingest records yet (D-ML99, L52b). Their dimensions carry <c>not_collected = true</c>, so a reader does not take
    /// the stored hits for the run's coverage.
    /// </summary>
    private static readonly HashSet<string> NotCollectedKinds = new(StringComparer.Ordinal)
    {
        CoverageKeys.RouteStatus, CoverageKeys.FaultTarget, CoverageKeys.ErrorCode, CoverageKeys.Transition,
    };

    private readonly ResultsService _results;
    private readonly IPathValidator _validator;
    private readonly string _labRoot;

    /// <summary>
    /// Creates the tool; the universe is read from the lab's own folder.
    /// </summary>
    /// <param name="results">The results service.</param>
    /// <param name="validator">Validates the universe path.</param>
    public CoverageReportTool(ResultsService results, IPathValidator validator)
        : this(results, validator, AppContext.BaseDirectory)
    {
    }

    /// <summary>
    /// Creates the tool with the folder that holds <c>coverage-universe.json</c> (tests point it at the source folder).
    /// </summary>
    /// <param name="results">The results service.</param>
    /// <param name="validator">Validates the universe path.</param>
    /// <param name="labRoot">The folder that holds <c>coverage-universe.json</c>.</param>
    internal CoverageReportTool(ResultsService results, IPathValidator validator, string labRoot)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _labRoot = labRoot ?? throw new ArgumentNullException(nameof(labRoot));
    }

    /// <inheritdoc />
    public override string Name => "coverage_report";

    /// <inheritdoc />
    public override string Description =>
        "Behavioural coverage over the runs since since_run_id (default all). kind: route_status, fault_target, error_code, invariant_check or transition. Returns {dimensions:[{kind,total,hit,unhit,not_collected}], outside_universe}. A dimension with not_collected = true is not fed yet (route_status, fault_target, error_code, transition until L52b): its unhit list is not coverage.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(CoverageReportArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.Kind is not null && !CoverageKeys.Kinds.Contains(args.Kind, StringComparer.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] kind must be one of " + string.Join(", ", CoverageKeys.Kinds));
        }

        Result<CoverageUniverse> universe = CoverageUniverse.Load(_labRoot, _validator);
        if (universe.IsFailure)
        {
            return Result<object>.Failure(universe.Error);
        }

        Result<IReadOnlyList<JsonObject>> rows = await _results
            .ReadAsync(db => db.CoverageSince(args.SinceRunId, args.Kind), ct).ConfigureAwait(false);
        if (rows.IsFailure)
        {
            return Result<object>.Failure(rows.Error);
        }

        var hits = new Dictionary<(string Kind, string Key), long>();
        foreach (JsonObject row in rows.Value)
        {
            hits[(row["kind"]!.GetValue<string>(), row["key"]!.GetValue<string>())] = row["hits"]!.GetValue<long>();
        }

        var dimensions = new List<object>();
        foreach (string kind in CoverageKeys.Kinds.Where(k => args.Kind is null || k == args.Kind))
        {
            dimensions.Add(Dimension(kind, universe.Value, hits));
        }

        var outside = hits.Where(hit => IsOutsideUniverse(hit.Key.Kind, hit.Key.Key))
            .OrderBy(hit => hit.Key.Kind, StringComparer.Ordinal).ThenBy(hit => hit.Key.Key, StringComparer.Ordinal)
            .Select(hit => new { kind = hit.Key.Kind, key = hit.Key.Key, hits = hit.Value })
            .ToList();

        return Result<object>.Success(new { since_run_id = args.SinceRunId, dimensions, outside_universe = outside });
    }

    /// <summary>
    /// One kind's dimension. A kind with a universe reports its total, its covered universe keys and the unhit ones; a kind
    /// without one (<c>fault_target</c>, <c>invariant_check</c>) reports its distinct hit keys and a null total.
    /// </summary>
    private static object Dimension(string kind, CoverageUniverse universe, IReadOnlyDictionary<(string Kind, string Key), long> hits)
    {
        List<string>? expected = ExpectedKeys(kind, universe);
        var kindHits = hits.Where(hit => hit.Key.Kind == kind && !IsOutsideUniverse(kind, hit.Key.Key))
            .Select(hit => hit.Key.Key).ToHashSet(StringComparer.Ordinal);

        bool notCollected = NotCollectedKinds.Contains(kind);
        if (expected is null)
        {
            return new { kind, total = (int?)null, hit = kindHits.Count, unhit = Array.Empty<string>(), not_collected = notCollected };
        }

        List<string> unhit = expected.Where(key => !Covered(kind, key, kindHits)).OrderBy(key => key, StringComparer.Ordinal).ToList();
        return new { kind, total = expected.Count, hit = expected.Count - unhit.Count, unhit, not_collected = notCollected };
    }

    /// <summary>The universe keys of a kind, or null when the universe does not list the kind.</summary>
    private static List<string>? ExpectedKeys(string kind, CoverageUniverse universe)
    {
        var keys = new List<string>();
        switch (kind)
        {
            case CoverageKeys.RouteStatus:
                foreach ((string service, CoverageService value) in universe.Services)
                {
                    foreach (CoverageRoute route in value.Routes)
                    {
                        foreach (string statusClass in UniverseStatusClasses)
                        {
                            keys.Add(service + " " + route.Method + " " + route.Template + " " + statusClass);
                        }
                    }
                }

                return keys;
            case CoverageKeys.ErrorCode:
                foreach ((string service, CoverageService value) in universe.Services)
                {
                    keys.AddRange(value.ErrorCodes.Select(code => CoverageKeys.ErrorCodeKey(service, code)));
                }

                return keys;
            case CoverageKeys.Transition:
                foreach (CoverageService value in universe.Services.Values)
                {
                    keys.AddRange(value.Transitions);
                }

                return keys;
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether an expected key is hit. A route whose universe method is <c>*</c> is covered by a hit of any method for the same
    /// service, template and status class.
    /// </summary>
    private static bool Covered(string kind, string expected, HashSet<string> kindHits)
    {
        if (kindHits.Contains(expected))
        {
            return true;
        }

        if (kind != CoverageKeys.RouteStatus)
        {
            return false;
        }

        string[] parts = expected.Split(' ');
        if (parts.Length != 4 || parts[1] != "*")
        {
            return false;
        }

        return kindHits.Any(hit =>
        {
            string[] observed = hit.Split(' ');
            return observed.Length == 4 && observed[0] == parts[0] && observed[2] == parts[2] && observed[3] == parts[3];
        });
    }

    /// <summary>A 5xx route key, or a key of a path that matched no template, is outside the universe (R-LOG: the path is never shown).</summary>
    private static bool IsOutsideUniverse(string kind, string key) =>
        kind == CoverageKeys.RouteStatus && (key.EndsWith(" 5xx", StringComparison.Ordinal) || key.Contains(" ?unmatched ", StringComparison.Ordinal));
}
