using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Probe;

/// <summary>
/// One request the sink recorded. <see cref="Seq"/> rises by one per recorded request, so a reader asks for
/// <c>since=&lt;last seq&gt;</c> and gets only what came after.
/// </summary>
/// <param name="Seq">Position in the sink's order, starting at 1.</param>
/// <param name="At">When the sink received the request (<see cref="TimeProvider"/>).</param>
/// <param name="Path">The request path, with the leading slash.</param>
/// <param name="Headers">Request headers with <c>Authorization</c> dropped and secret-bearing values redacted.</param>
/// <param name="Body">The request body, redacted and cut to <see cref="SinkRecorder.MaxBodyChars"/>.</param>
public sealed record SinkRequest(
    long Seq,
    DateTimeOffset At,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

/// <summary>
/// The in-memory store behind the lab's webhook and email sink (C10 <c>sink</c>, D-ML40 not ruled). It keeps the last
/// <see cref="MaxRecords"/> requests. It drops <c>Authorization</c> and redacts credentials before anything is stored,
/// so no reader of <c>/_sink/requests</c> can see a bearer token or an admin key.
/// </summary>
public sealed class SinkRecorder
{
    /// <summary>How many requests the sink keeps; older ones fall off the front.</summary>
    public const int MaxRecords = 500;

    /// <summary>Largest body kept, in characters.</summary>
    public const int MaxBodyChars = 64 * 1024;

    /// <summary>Header names whose values are never stored.</summary>
    private static readonly IReadOnlySet<string> RedactedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "X-Admin-Api-Key", "Cookie", "Set-Cookie", "Proxy-Authorization",
    };

    /// <summary>Header names that are dropped entirely.</summary>
    private static readonly IReadOnlySet<string> DroppedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
    };

    /// <summary>Header names that carry credentials by name, even when they are not in <see cref="RedactedHeaders"/>.</summary>
    private static readonly Regex SensitiveHeaderName = new(
        @"(?i)(key|token|secret|auth|passw|cookie|signature|credential|session)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Key/value credential pairs. A quoted value runs to its closing quote (escaped quotes included), so a spaced
    /// password or a JSON string nested in a JSON string is redacted whole.
    /// </summary>
    private static readonly Regex SecretPair = new(
        @"(?i)(?<pre>(?:access_token|refresh_token|id_token|password|passwd|client_secret|secret|api[_-]?key|token|authorization|private_key)\\?""?\s*[:=]\s*)(?:(?<q>\\?"")(?:[^""\\]|\\.)*(?<qe>\\?""|\\?\z)|[^""&,\s}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerValue = new(
        @"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BasicValue = new(
        @"(?i)(basic\s+)[A-Za-z0-9+/=]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PemBlock = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?(?:-----END [A-Z ]*PRIVATE KEY-----|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The token segment of a Discord-style webhook path (<c>/api/webhooks/{id}/{token}</c>, versioned or not).</summary>
    private static readonly Regex WebhookTokenPath = new(
        @"(?i)(/webhooks/\d+/)[^/?#]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly Queue<SinkRequest> _items = new();
    private readonly TimeProvider _time;
    private long _seq;

    /// <summary>Creates an empty recorder that stamps requests with <paramref name="time"/>.</summary>
    public SinkRecorder(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Records one request. The headers and body are cleaned first, so the stored copy is the only copy kept.
    /// </summary>
    public SinkRequest Record(string path, IEnumerable<KeyValuePair<string, string>> headers, string body)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);

        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (DroppedHeaders.Contains(header.Key))
            {
                continue;
            }

            bool secretName = RedactedHeaders.Contains(header.Key) || SensitiveHeaderName.IsMatch(header.Key);
            cleaned[header.Key] = secretName ? "[redacted]" : Redact(header.Value);
        }

        string kept = Bound(Redact(body));
        string storedPath = Bound(Redact(WebhookTokenPath.Replace(path, "$1[redacted]")));

        lock (_gate)
        {
            _seq++;
            var request = new SinkRequest(_seq, _time.GetUtcNow(), storedPath, cleaned, kept);
            _items.Enqueue(request);
            while (_items.Count > MaxRecords)
            {
                _items.Dequeue();
            }

            return request;
        }
    }

    /// <summary>Returns the recorded requests with <c>Seq</c> greater than <paramref name="since"/>, oldest first.</summary>
    public IReadOnlyList<SinkRequest> Since(long since)
    {
        lock (_gate)
        {
            return _items.Where(item => item.Seq > since).ToList();
        }
    }

    /// <summary>Forgets every recorded request. The sequence keeps counting, so an old <c>since</c> stays valid.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }

    /// <summary>
    /// Replaces credential-shaped text (key=value, JSON pairs, bearer and basic tokens, PEM private keys) with <c>[redacted]</c>.
    /// A quoted value keeps its quotes: <c>"password":"[redacted]"</c>.
    /// </summary>
    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string withPem = PemBlock.Replace(text, "[redacted]");
        string withBasic = BasicValue.Replace(withPem, match => match.Groups[1].Value + "[redacted]");
        string withBearer = BearerValue.Replace(withBasic, match => match.Groups[1].Value + "[redacted]");
        return SecretPair.Replace(withBearer, RedactPair);
    }

    private static string RedactPair(Match match)
    {
        string prefix = match.Groups["pre"].Value;
        if (match.Groups["q"].Success)
        {
            return prefix + match.Groups["q"].Value + "[redacted]" + match.Groups["qe"].Value;
        }

        return prefix + "[redacted]";
    }

    private static string Bound(string text) => text.Length > MaxBodyChars ? text[..MaxBodyChars] : text;
}
