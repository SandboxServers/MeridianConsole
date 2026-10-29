using System.Text.Json;
using Dhadgar.Lab.Results;

namespace Dhadgar.Lab.Fuzz;

/// <summary>What one fuzz request observed. <see cref="Error"/> is set when no response came back.</summary>
/// <param name="Status">The HTTP status, or 0 when there was no response.</param>
/// <param name="ContentType">The response <c>Content-Type</c>, or null.</param>
/// <param name="BodyText">The response body (already capped by <c>LabHttp</c>). It is never logged or stored.</param>
/// <param name="DurationMs">The request duration.</param>
/// <param name="Error">The transport error, or null.</param>
public sealed record FuzzObservation(int Status, string? ContentType, string BodyText, long DurationMs, string? Error);

/// <summary>The oracle's judgement of one case.</summary>
public enum FuzzVerdictKind
{
    /// <summary>The case met every rule.</summary>
    Ok,

    /// <summary>The case broke a rule, and the rule's signature matched an open known issue.</summary>
    Known,

    /// <summary>The case broke a rule and no open known issue matched. It becomes a row.</summary>
    Failure,

    /// <summary>The Gateway answered 429. Not a failure, no row; the job retries once after <c>Retry-After</c>.</summary>
    Throttled,
}

/// <summary>The oracle's result: the kind, the reasons (no bodies), the signatures of the failing rule and the issue id when known.</summary>
public sealed record FuzzVerdict(FuzzVerdictKind Kind, IReadOnlyList<string> Reasons, IReadOnlyList<FailureSignature> Signatures, string? KnownIssueId);

/// <summary>
/// The per-case oracle (L28 file 3, F29). Rules: the status is never 500-599; a 4xx has <c>application/problem+json</c> with
/// <c>type</c>, <c>title</c> and <c>status</c>; the latency is under 10 s; no vault value or daemon token is in the response.
/// A 429 is throttled. A failing case's <c>http</c> signature goes to <see cref="KnownIssues.Match"/>, and a match makes the
/// case <see cref="FuzzVerdictKind.Known"/>. A 5xx is never offered to the known-issue list (KI-F69's pattern is 4xx only).
/// </summary>
public static class FuzzOracle
{
    /// <summary>The latency limit: a case at or over 10 s fails.</summary>
    public const long LatencyLimitMs = 10_000;

    /// <summary>The section name of fuzz rows (row ids are <c>fuzz/&lt;target&gt;/&lt;n&gt;</c>).</summary>
    public const string Section = "fuzz";

    private const string ProblemJson = "application/problem+json";

    /// <summary>
    /// Judges one observed case. <paramref name="forbiddenMarkers"/> are secret values (vault values, the daemon token) that must
    /// not appear in the response; an empty marker is ignored.
    /// </summary>
    public static FuzzVerdict Judge(
        FuzzTarget target,
        FuzzCase fuzzCase,
        FuzzObservation observation,
        KnownIssues known,
        IReadOnlyList<string> forbiddenMarkers)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fuzzCase);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(forbiddenMarkers);

        if (observation.Status == 429)
        {
            return new FuzzVerdict(FuzzVerdictKind.Throttled, [], [], null);
        }

        var reasons = new List<string>();
        string? problemCode = null;

        if (observation.Status == 0 || observation.Error is not null)
        {
            reasons.Add("no response");
        }

        if (observation.Status is >= 500 and <= 599)
        {
            reasons.Add("status " + observation.Status + " is a server error");
        }
        else if (observation.Status is >= 400 and <= 499)
        {
            (bool isProblem, string? code) = ReadProblem(observation);
            problemCode = code;
            if (!isProblem)
            {
                reasons.Add(observation.Status + " without application/problem+json type, title and status");
            }
        }

        if (observation.DurationMs >= LatencyLimitMs)
        {
            reasons.Add("latency " + observation.DurationMs + " ms is at or over 10 s");
        }

        bool leaked = forbiddenMarkers.Any(marker => marker.Length > 0 && observation.BodyText.Contains(marker, StringComparison.Ordinal));
        if (leaked)
        {
            reasons.Add("a secret value is in the response");
        }

        if (reasons.Count == 0)
        {
            return new FuzzVerdict(FuzzVerdictKind.Ok, [], [], null);
        }

        FailureSignature signature = SignatureBuilder.Http(new HttpEvidence(
            target.Service, observation.Status, problemCode, target.Method, target.PathTemplate, target.PathTemplate, []));
        var signatures = new List<FailureSignature> { signature };

        // A 5xx, or a secret leak, is never excused by a known issue (KI-F69's pattern is 4xx; a leak is never known).
        bool excusable = observation.Status is >= 400 and <= 499 && !leaked && observation.Error is null;
        if (excusable)
        {
            KnownMatch match = known.Match(new RowFailure(Section, RowId(target, fuzzCase), null, signatures));
            if (string.Equals(match.State, "known", StringComparison.Ordinal))
            {
                return new FuzzVerdict(FuzzVerdictKind.Known, reasons, signatures, match.IssueId);
            }
        }

        return new FuzzVerdict(FuzzVerdictKind.Failure, reasons, signatures, null);
    }

    /// <summary>The row id of a case: <c>fuzz/&lt;target&gt;/&lt;n&gt;</c>.</summary>
    public static string RowId(FuzzTarget target, FuzzCase fuzzCase)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fuzzCase);
        return "fuzz/" + target.Name + "/" + fuzzCase.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (bool IsProblem, string? ErrorCode) ReadProblem(FuzzObservation observation)
    {
        string? mediaType = observation.ContentType?.Split(';')[0].Trim();
        if (!string.Equals(mediaType, ProblemJson, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(observation.BodyText);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out _)
                || !root.TryGetProperty("title", out _)
                || !root.TryGetProperty("status", out _))
            {
                return (false, null);
            }

            string? code = root.TryGetProperty("errorCode", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            return (true, code);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }
}
