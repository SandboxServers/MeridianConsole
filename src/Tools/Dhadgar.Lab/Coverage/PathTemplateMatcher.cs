namespace Dhadgar.Lab.Coverage;

/// <summary>
/// Matches a concrete request path to the most specific template of a service (L52). A <c>{name}</c> segment matches one
/// non-empty segment; a <c>{**name}</c> segment matches the rest of the path. A template with no catch-all beats one with
/// a catch-all, then the template with more literal segments wins, then the one with fewer placeholders. A request that
/// matches nothing is keyed as <c>&lt;service&gt; &lt;METHOD&gt; ?unmatched &lt;class&gt;</c> (see <see cref="UnmatchedKey"/>).
/// </summary>
public static class PathTemplateMatcher
{
    /// <summary>
    /// The template of the most specific route that matches <paramref name="method"/> and <paramref name="requestPath"/>,
    /// or null when none does. The query string is ignored.
    /// </summary>
    /// <param name="routes">The routes of one service.</param>
    /// <param name="method">The request method.</param>
    /// <param name="requestPath">The concrete request path.</param>
    public static string? MatchTemplate(IEnumerable<CoverageRoute> routes, string method, string requestPath)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestPath);

        string[] segments = SegmentsOf(requestPath);
        string? best = null;
        (bool NoCatchAll, int Literals, int Placeholders) bestScore = default;
        foreach (CoverageRoute route in routes)
        {
            if (route.Method != "*" && !string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryScore(route.Template, segments, out (bool NoCatchAll, int Literals, int Placeholders) score))
            {
                continue;
            }

            if (best is null || Compare(score, bestScore) > 0)
            {
                best = route.Template;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// The key of a request that matched no template: <c>&lt;service&gt; &lt;METHOD&gt; ?unmatched &lt;class&gt;</c>. The key
    /// never holds the path (R-LOG).
    /// </summary>
    /// <param name="service">The service name.</param>
    /// <param name="method">The request method.</param>
    /// <param name="statusClass">The status class, for example <c>5xx</c>.</param>
    public static string UnmatchedKey(string service, string method, string statusClass) =>
        service + " " + method.ToUpperInvariant() + " ?unmatched " + statusClass;

    /// <summary>
    /// The status class of an HTTP status code: <c>2xx</c>, <c>4xx</c> and so on.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    public static string StatusClass(int statusCode) => (statusCode / 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "xx";

    private static bool TryScore(string template, string[] segments, out (bool NoCatchAll, int Literals, int Placeholders) score)
    {
        string[] parts = SegmentsOf(template);
        int literals = 0;
        int placeholders = 0;
        bool catchAll = false;
        score = default;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (IsCatchAll(part))
            {
                catchAll = true;
                // A catch-all takes the rest; it must be the last segment and may match zero segments.
                if (i != parts.Length - 1)
                {
                    return false;
                }

                score = (false, literals, placeholders);
                return segments.Length >= i;
            }

            if (i >= segments.Length)
            {
                return false;
            }

            if (IsPlaceholder(part))
            {
                if (segments[i].Length == 0)
                {
                    return false;
                }

                placeholders++;
                continue;
            }

            if (!string.Equals(part, segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            literals++;
        }

        if (segments.Length != parts.Length)
        {
            return false;
        }

        score = (!catchAll, literals, placeholders);
        return true;
    }

    private static int Compare((bool NoCatchAll, int Literals, int Placeholders) left, (bool NoCatchAll, int Literals, int Placeholders) right)
    {
        if (left.NoCatchAll != right.NoCatchAll)
        {
            return left.NoCatchAll ? 1 : -1;
        }

        if (left.Literals != right.Literals)
        {
            return left.Literals.CompareTo(right.Literals);
        }

        return right.Placeholders.CompareTo(left.Placeholders);
    }

    private static bool IsPlaceholder(string part) => part.Length > 2 && part[0] == '{' && part[^1] == '}' && part[1] != '*';

    private static bool IsCatchAll(string part) => part.Length > 3 && part.StartsWith("{**", StringComparison.Ordinal) && part[^1] == '}';

    private static string[] SegmentsOf(string path)
    {
        int query = path.IndexOfAny(['?', '#']);
        string trimmed = query >= 0 ? path[..query] : path;
        return trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
