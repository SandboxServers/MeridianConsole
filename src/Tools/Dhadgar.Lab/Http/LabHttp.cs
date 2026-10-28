using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Http;

/// <summary>
/// The result of <c>http_request</c>: status, headers and body (redacted), the duration, and <c>outcome</c>. A timeout or a
/// transport error after the request was built is a result with <c>status: 0</c> and <c>error</c>, not a tool failure.
/// </summary>
public sealed record HttpProbeResult(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    JsonElement? Body,
    long DurationMs,
    string Outcome,
    string? Error);

/// <summary>
/// Lab-only HTTP probes (C4 <c>http_request</c>). Every URL is composed on the loopback host and checked with
/// <see cref="LabHosts.IsLabEndpoint"/> before the request (R-HOST). Redirects are never followed. <c>${secret:&lt;ref&gt;}</c>
/// placeholders in the body are resolved inside the vault right before sending; the request body is never logged or returned.
/// </summary>
public sealed class LabHttp
{
    /// <summary>The name of the named <see cref="HttpClient"/> registered for the probes.</summary>
    public const string ClientName = "lab-probe";

    /// <summary>The request timeout. A timeout is returned as a result, not thrown.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The largest request body accepted (1 MiB).</summary>
    public const int MaxRequestBytes = 1024 * 1024;

    /// <summary>The largest response body returned (64 KiB).</summary>
    public const int MaxResponseBytes = 64 * 1024;

    private const string LoopbackHost = "127.0.0.1";
    private const string SecretPrefix = "${secret:";
    private const int MaxPathLength = 2048;

    private static readonly Dictionary<string, int> ServiceTargets = new(StringComparer.Ordinal)
    {
        ["gateway"] = LabPorts.Gateway,
        ["identity"] = LabPorts.Identity,
        ["nodes"] = LabPorts.Nodes,
        ["notifications"] = LabPorts.Notifications,
        ["discord"] = LabPorts.Discord,
        ["betterauth"] = LabPorts.BetterAuth,
    };

    private static readonly HashSet<string> RefusedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Type", "Content-Length", "Transfer-Encoding",
    };

    private readonly IHttpClientFactory _factory;
    private readonly SecretVault _vault;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the probe client over the named HTTP client, the vault and the redactor.
    /// </summary>
    public LabHttp(IHttpClientFactory factory, SecretVault vault, Redactor redactor, TimeProvider time)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The primary handler for the probes. Redirects are never followed; the caller sees the 3xx response.
    /// </summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler { AllowAutoRedirect = false };

    /// <summary>
    /// Sends one probe to <paramref name="via"/> (<c>gateway</c>, <c>agent_ingress</c> or a lab service name).
    /// </summary>
    public async Task<Result<HttpProbeResult>> SendAsync(
        string via,
        string method,
        string path,
        IReadOnlyDictionary<string, string>? headers,
        JsonElement? body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(via);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        Result<Uri> uri = Compose(via, path);
        if (uri.IsFailure)
        {
            return Result<HttpProbeResult>.Failure(uri.Error);
        }

        Result<HttpMethod> verb = ParseMethod(method);
        if (verb.IsFailure)
        {
            return Result<HttpProbeResult>.Failure(verb.Error);
        }

        if (headers is not null && headers.Keys.Any(RefusedHeaders.Contains))
        {
            return Result<HttpProbeResult>.Failure("[Lab.InvalidArgument] Host, Content-Type, Content-Length and Transfer-Encoding cannot be set");
        }

        byte[]? payload = null;
        try
        {
            if (body is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) })
            {
                Result<byte[]> built = ResolveBody(body.Value.GetRawText());
                if (built.IsFailure)
                {
                    return Result<HttpProbeResult>.Failure(built.Error);
                }

                payload = built.Value;
            }

            using var request = new HttpRequestMessage(verb.Value, uri.Value);
            if (headers is not null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        return Result<HttpProbeResult>.Failure("[Lab.InvalidArgument] a header name or value is not valid");
                    }
                }
            }

            if (payload is not null)
            {
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return await SendPreparedAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    /// <summary>
    /// Composes the probe URI on the loopback host and refuses anything that is not a lab endpoint (<c>[Lab.Untrusted]</c>).
    /// </summary>
    internal static Result<Uri> Compose(string via, string path)
    {
        int port;
        if (string.Equals(via, "gateway", StringComparison.Ordinal))
        {
            port = LabPorts.Gateway;
        }
        else if (string.Equals(via, "agent_ingress", StringComparison.Ordinal))
        {
            port = LabPorts.AgentIngress;
        }
        else if (!string.IsNullOrEmpty(via) && ServiceTargets.TryGetValue(via, out int servicePort))
        {
            port = servicePort;
        }
        else
        {
            return Result<Uri>.Failure("[Lab.NotFound] no lab target for that via value");
        }

        if (!path.StartsWith('/') || path.Length > MaxPathLength)
        {
            return Result<Uri>.Failure("[Lab.InvalidArgument] path must start with / and be at most 2048 characters");
        }

        Uri target;
        try
        {
            target = new Uri(new Uri($"http://{LoopbackHost}:{port}/", UriKind.Absolute), path);
        }
        catch (UriFormatException)
        {
            return Result<Uri>.Failure("[Lab.InvalidArgument] path is not a valid URI path");
        }

        return LabHosts.IsLabEndpoint(target)
            ? Result<Uri>.Success(target)
            : Result<Uri>.Failure("[Lab.Untrusted] the composed URI is not a lab endpoint");
    }

    /// <summary>
    /// Replaces each <c>${secret:&lt;ref&gt;}</c> with the vault value, JSON-escaped, and returns the UTF-8 body. The copies of
    /// the secret characters are zeroed before returning.
    /// </summary>
    internal Result<byte[]> ResolveBody(string template)
    {
        if (Encoding.UTF8.GetByteCount(template) > MaxRequestBytes)
        {
            return Result<byte[]>.Failure("[Lab.InvalidArgument] body is larger than 1 MiB");
        }

        var text = new StringBuilder(template.Length);
        int position = 0;
        while (true)
        {
            int start = template.IndexOf(SecretPrefix, position, StringComparison.Ordinal);
            if (start < 0)
            {
                _ = text.Append(template, position, template.Length - position);
                break;
            }

            int close = template.IndexOf('}', start);
            if (close < 0)
            {
                return Result<byte[]>.Failure("[Lab.InvalidArgument] unterminated ${secret:} placeholder");
            }

            string reference = template.Substring(start + SecretPrefix.Length, close - start - SecretPrefix.Length);
            if (reference.Length == 0 || !reference.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return Result<byte[]>.Failure("[Lab.InvalidArgument] placeholder is not a vault reference");
            }

            Result<char[]> value = _vault.Use(reference, span => span.ToArray());
            if (value.IsFailure)
            {
                return Result<byte[]>.Failure(value.Error);
            }

            try
            {
                _ = text.Append(template, position, start - position);
                AppendJsonEscaped(text, value.Value!);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.Value!.AsSpan()));
            }

            position = close + 1;
        }

        return Result<byte[]>.Success(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static void AppendJsonEscaped(StringBuilder target, ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': _ = target.Append("\\\""); break;
                case '\\': _ = target.Append("\\\\"); break;
                case < ' ': _ = target.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: _ = target.Append(c); break;
            }
        }
    }

    private static Result<HttpMethod> ParseMethod(string method)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return Result<HttpMethod>.Success(HttpMethod.Get);
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) return Result<HttpMethod>.Success(HttpMethod.Post);
        if (string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)) return Result<HttpMethod>.Success(HttpMethod.Put);
        if (string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)) return Result<HttpMethod>.Success(HttpMethod.Patch);
        if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)) return Result<HttpMethod>.Success(HttpMethod.Delete);
        return Result<HttpMethod>.Failure("[Lab.InvalidArgument] method must be GET, POST, PUT, PATCH or DELETE");
    }

    private async Task<Result<HttpProbeResult>> SendPreparedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpClient client = _factory.CreateClient(ClientName);
        long started = _time.GetTimestamp();
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectHeaders(response.Headers, headers);
            CollectHeaders(response.Content.Headers, headers);

            (string text, bool truncated) = await ReadCappedAsync(response, ct).ConfigureAwait(false);
            JsonElement? body = ToBody(text, truncated, response.Content.Headers.ContentType?.MediaType);
            int status = (int)response.StatusCode;
            return Result<HttpProbeResult>.Success(new HttpProbeResult(
                status,
                headers,
                body,
                Elapsed(started),
                status is >= 200 and < 300 ? "ok" : "fail",
                null));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<HttpProbeResult>.Success(new HttpProbeResult(0, new Dictionary<string, string>(), null, Elapsed(started), "unknown", "timeout"));
        }
        catch (HttpRequestException)
        {
            // The message can carry the URL and the peer; only the code is returned.
            return Result<HttpProbeResult>.Success(new HttpProbeResult(0, new Dictionary<string, string>(), null, Elapsed(started), "unknown", "transport"));
        }
    }

    private void CollectHeaders(HttpHeaders source, Dictionary<string, string> target)
    {
        foreach (KeyValuePair<string, IEnumerable<string>> header in source)
        {
            target[header.Key] = _redactor.Redact(string.Join(", ", header.Value));
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[MaxResponseBytes + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        bool truncated = total > MaxResponseBytes;
        string text = Encoding.UTF8.GetString(buffer, 0, Math.Min(total, MaxResponseBytes));
        return (text, truncated);
    }

    private JsonElement? ToBody(string text, bool truncated, string? mediaType)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (!truncated && mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                return _redactor.Redact(document.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Not JSON after all: fall through and return the text.
            }
        }

        return JsonSerializer.SerializeToElement(_redactor.Redact(text));
    }

    private long Elapsed(long started) => (long)_time.GetElapsedTime(started).TotalMilliseconds;
}
