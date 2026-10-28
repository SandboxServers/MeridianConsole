using System.Net;
using System.Text.Json;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// One Toxiproxy proxy as the API reports it.
/// </summary>
/// <param name="Name">The proxy name.</param>
/// <param name="Listen">The listen address inside the container.</param>
/// <param name="Upstream">The upstream address.</param>
/// <param name="Enabled">Whether the proxy forwards traffic.</param>
public sealed record ProxySpec(string Name, string Listen, string Upstream, bool Enabled);

/// <summary>
/// One toxic to add to a proxy. Toxic names are <c>&lt;fault_id&gt;-&lt;stream&gt;</c>. <see cref="ToxicValidator"/> checks it before it is sent.
/// </summary>
/// <param name="Name">The toxic name.</param>
/// <param name="Type">The toxic type (<c>latency</c>, <c>bandwidth</c>, and so on).</param>
/// <param name="Stream">The direction: <c>upstream</c> or <c>downstream</c>.</param>
/// <param name="Toxicity">The fraction of connections the toxic applies to, 0.0 to 1.0.</param>
/// <param name="Attributes">The type-specific attributes, all integers.</param>
public sealed record ToxicSpec(string Name, string Type, string Stream, double Toxicity, IReadOnlyDictionary<string, long> Attributes);

/// <summary>
/// Minimal Toxiproxy API client (L08). Only the loopback API port in <see cref="LabPorts.ToxiproxyApi"/> is reachable.
/// </summary>
public sealed class ToxiproxyClient : IDisposable
{
    private static readonly Uri BaseUri = new($"http://127.0.0.1:{LabPorts.ToxiproxyApi}/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpMessageHandler _handler;
    private readonly HttpClient _http;

    /// <summary>
    /// Creates the client. Tests pass a fake handler; the default handler never follows redirects.
    /// </summary>
    public ToxiproxyClient(HttpMessageHandler? handler = null)
    {
        _handler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        _http = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = BaseUri,
            Timeout = RequestTimeout,
        };
    }

    /// <summary>
    /// True when <c>GET /version</c> answers 200.
    /// </summary>
    public async Task<Result<bool>> IsReadyAsync(CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, "version", null, ct).ConfigureAwait(false);
        return response.IsFailure
            ? Result<bool>.Failure(response.Error)
            : Result<bool>.Success(response.Value.Status == HttpStatusCode.OK);
    }

    /// <summary>
    /// Creates a proxy. A proxy that already exists (409) counts as created.
    /// </summary>
    public async Task<Result> CreateProxyAsync(ProxySpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!ProxyName.IsMatch(spec.Name))
        {
            return Result.Failure("[Lab.InvalidArgument] proxy name is not allowed");
        }

        var body = JsonSerializer.Serialize(new { name = spec.Name, listen = spec.Listen, upstream = spec.Upstream, enabled = spec.Enabled });
        var response = await SendAsync(HttpMethod.Post, "proxies", body, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result.Failure(response.Error);
        }

        return response.Value.Status is HttpStatusCode.Created or HttpStatusCode.Conflict
            ? Result.Success()
            : Result.Failure("[Lab.Unavailable] Toxiproxy refused the proxy");
    }

    /// <summary>
    /// Lists every proxy the API reports.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProxySpec>>> ListProxiesAsync(CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, "proxies", null, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<ProxySpec>>.Failure(response.Error);
        }

        if (response.Value.Status != HttpStatusCode.OK)
        {
            return Result<IReadOnlyList<ProxySpec>>.Failure("[Lab.Unavailable] Toxiproxy could not list proxies");
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value.Body);
            var proxies = new List<ProxySpec>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value;
                proxies.Add(new ProxySpec(
                    value.GetProperty("name").GetString() ?? property.Name,
                    value.GetProperty("listen").GetString() ?? string.Empty,
                    value.GetProperty("upstream").GetString() ?? string.Empty,
                    value.GetProperty("enabled").GetBoolean()));
            }

            return Result<IReadOnlyList<ProxySpec>>.Success(proxies);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result<IReadOnlyList<ProxySpec>>.Failure("[Lab.Unavailable] Toxiproxy returned an unexpected body");
        }
    }

    /// <summary>
    /// Removes every toxic and re-enables every proxy (<c>POST /reset</c>).
    /// </summary>
    public async Task<Result> ResetAsync(CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Post, "reset", null, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result.Failure(response.Error);
        }

        return response.Value.Status == HttpStatusCode.NoContent || response.Value.Status == HttpStatusCode.OK
            ? Result.Success()
            : Result.Failure("[Lab.Unavailable] Toxiproxy reset failed");
    }

    /// <summary>
    /// Enables or disables one proxy (<c>POST /proxies/{name}</c>). A missing proxy maps to <c>[Lab.NotFound]</c>.
    /// </summary>
    public async Task<Result> SetEnabledAsync(string proxy, bool enabled, CancellationToken ct)
    {
        if (!ProxyName.IsMatch(proxy))
        {
            return Result.Failure("[Lab.InvalidArgument] proxy name is not allowed");
        }

        var body = JsonSerializer.Serialize(new { enabled });
        var response = await SendAsync(HttpMethod.Post, $"proxies/{proxy}", body, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result.Failure(response.Error);
        }

        return response.Value.Status switch
        {
            HttpStatusCode.OK => Result.Success(),
            HttpStatusCode.NotFound => Result.Failure("[Lab.NotFound] no such proxy"),
            _ => Result.Failure("[Lab.Unavailable] Toxiproxy refused the proxy change"),
        };
    }

    /// <summary>
    /// Adds a toxic to one proxy (<c>POST /proxies/{name}/toxics</c>). The toxic is validated by <see cref="ToxicValidator"/> first.
    /// </summary>
    public async Task<Result> AddToxicAsync(string proxy, ToxicSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!ProxyName.IsMatch(proxy) || !ToxicName.IsMatch(spec.Name))
        {
            return Result.Failure("[Lab.InvalidArgument] proxy or toxic name is not allowed");
        }

        var valid = ToxicValidator.Validate(spec.Type, spec.Stream, spec.Toxicity, spec.Attributes);
        if (valid.IsFailure)
        {
            return valid;
        }

        var body = JsonSerializer.Serialize(new
        {
            name = spec.Name,
            type = spec.Type,
            stream = spec.Stream,
            toxicity = spec.Toxicity,
            attributes = spec.Attributes,
        });
        var response = await SendAsync(HttpMethod.Post, $"proxies/{proxy}/toxics", body, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result.Failure(response.Error);
        }

        return response.Value.Status switch
        {
            HttpStatusCode.OK => Result.Success(),
            HttpStatusCode.NotFound => Result.Failure("[Lab.NotFound] no such proxy"),
            _ => Result.Failure("[Lab.Unavailable] Toxiproxy refused the toxic"),
        };
    }

    /// <summary>
    /// Removes one toxic (<c>DELETE /proxies/{name}/toxics/{toxic}</c>). A missing toxic or proxy maps to <c>[Lab.NotFound]</c>.
    /// </summary>
    public async Task<Result> RemoveToxicAsync(string proxy, string toxicName, CancellationToken ct)
    {
        if (!ProxyName.IsMatch(proxy) || !ToxicName.IsMatch(toxicName))
        {
            return Result.Failure("[Lab.InvalidArgument] proxy or toxic name is not allowed");
        }

        var response = await SendAsync(HttpMethod.Delete, $"proxies/{proxy}/toxics/{toxicName}", null, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result.Failure(response.Error);
        }

        return response.Value.Status switch
        {
            HttpStatusCode.OK or HttpStatusCode.NoContent => Result.Success(),
            HttpStatusCode.NotFound => Result.Failure("[Lab.NotFound] no such toxic"),
            _ => Result.Failure("[Lab.Unavailable] Toxiproxy refused the toxic removal"),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private static readonly System.Text.RegularExpressions.Regex ProxyName =
        new("^[a-z0-9][a-z0-9-]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Toxic names are <fault_id>-<stream>; they go into a URL path, so only path-safe characters pass.
    private static readonly System.Text.RegularExpressions.Regex ToxicName =
        new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private async Task<Result<(HttpStatusCode Status, string Body)>> SendAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        var uri = new Uri(BaseUri, path);
        if (!LabHosts.IsLabInfraEndpoint(uri))
        {
            return Result<(HttpStatusCode, string)>.Failure("[Lab.Untrusted] Toxiproxy address is not the lab API");
        }

        using var request = new HttpRequestMessage(method, uri);
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result<(HttpStatusCode, string)>.Success((response.StatusCode, body));
        }
        catch (HttpRequestException)
        {
            return Result<(HttpStatusCode, string)>.Failure("[Lab.Unavailable] Toxiproxy is not reachable");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<(HttpStatusCode, string)>.Failure("[Lab.Unavailable] Toxiproxy did not answer in time");
        }
    }
}
