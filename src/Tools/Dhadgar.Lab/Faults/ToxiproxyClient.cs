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

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private static readonly System.Text.RegularExpressions.Regex ProxyName =
        new("^[a-z0-9][a-z0-9-]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
