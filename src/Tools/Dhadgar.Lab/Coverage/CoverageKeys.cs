using Dhadgar.Lab.Results;

namespace Dhadgar.Lab.Coverage;

/// <summary>
/// The C18 behavioural coverage keys (L52). A key holds templates, status classes, error codes, fault kinds and target ids,
/// and check ids only: never a concrete path, a body or a header (R-LOG). The kinds are the C18 table's rows.
/// </summary>
public static class CoverageKeys
{
    /// <summary>A request status, keyed by service, method, route template and status class.</summary>
    public const string RouteStatus = "route_status";

    /// <summary>A fault, keyed by fault kind and target id.</summary>
    public const string FaultTarget = "fault_target";

    /// <summary>An error code, keyed by service and code.</summary>
    public const string ErrorCode = "error_code";

    /// <summary>An invariant check that ran, keyed by invariant and check id.</summary>
    public const string InvariantCheck = "invariant_check";

    /// <summary>An entity state change between two row-end snapshots.</summary>
    public const string Transition = "transition";

    /// <summary>The kinds, in report order.</summary>
    public static readonly IReadOnlyList<string> Kinds =
        [RouteStatus, FaultTarget, ErrorCode, InvariantCheck, Transition];

    /// <summary>
    /// The route key of one response: <c>&lt;service&gt; &lt;METHOD&gt; &lt;template&gt; &lt;class&gt;</c> when the path matches a
    /// template of the service, <c>&lt;service&gt; &lt;METHOD&gt; ?unmatched &lt;class&gt;</c> when it does not. Null for a service the
    /// universe does not name, or a status outside 2xx, 4xx and 5xx (1xx and 3xx are not counted).
    /// </summary>
    /// <param name="universe">The coverage universe.</param>
    /// <param name="service">The service name.</param>
    /// <param name="method">The request method.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="requestPath">The concrete request path. It is matched, never stored.</param>
    public static string? RouteKey(CoverageUniverse universe, string service, string method, int statusCode, string requestPath)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestPath);

        if (statusCode is < 200 or (>= 300 and < 400) or > 599)
        {
            return null;
        }

        if (!universe.Services.TryGetValue(service, out CoverageService? known))
        {
            return null;
        }

        string statusClass = PathTemplateMatcher.StatusClass(statusCode);
        string? template = PathTemplateMatcher.MatchTemplate(known.Routes, method, requestPath);
        return template is null
            ? PathTemplateMatcher.UnmatchedKey(service, method, statusClass)
            : service + " " + method.ToUpperInvariant() + " " + template + " " + statusClass;
    }

    /// <summary>The fault key: <c>&lt;fault kind&gt;@&lt;target id&gt;</c>.</summary>
    public static string FaultKey(string kind, string targetId) => kind + "@" + targetId;

    /// <summary>The error-code key: <c>&lt;service&gt;:&lt;errorCode&gt;</c>.</summary>
    public static string ErrorCodeKey(string service, string errorCode) => service + ":" + errorCode;

    /// <summary>The invariant key: <c>&lt;invariant&gt;/&lt;check id&gt;</c>.</summary>
    public static string InvariantKey(string invariantId, string checkId) => invariantId + "/" + checkId;

    /// <summary>The transition key: <c>&lt;entity&gt;:&lt;from&gt;-&gt;&lt;to&gt;</c>, the form the universe lists.</summary>
    public static string TransitionKey(string entity, string from, string to) => entity + ":" + from + "->" + to;

    /// <summary>
    /// The keys of one run's hits, each with its count. Keys are unique per kind and run.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="hits">The hits as (kind, key) pairs, one pair per hit.</param>
    public static IReadOnlyList<CoverageRecord> Count(string runId, IEnumerable<(string Kind, string Key)> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        return hits.GroupBy(hit => hit, hit => hit)
            .Select(group => new CoverageRecord(runId, group.Key.Kind, group.Key.Key, group.LongCount()))
            .OrderBy(record => record.Kind, StringComparer.Ordinal)
            .ThenBy(record => record.Key, StringComparer.Ordinal)
            .ToList();
    }
}
