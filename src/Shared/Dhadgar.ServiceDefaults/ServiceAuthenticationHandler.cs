using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults;

public sealed class ServiceAuthenticationOptions
{
    public string TokenEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public string? Audience { get; set; }
}

public interface IServiceTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken ct = default);
}

public sealed class ServiceTokenProvider : IServiceTokenProvider, IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IOptions<ServiceAuthenticationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public ServiceTokenProvider(
        HttpClient httpClient,
        IOptions<ServiceAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cachedToken is not null && now < _expiresAt - RefreshSkew)
        {
            return _cachedToken;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_cachedToken is not null && now < _expiresAt - RefreshSkew)
            {
                return _cachedToken;
            }

            var options = _options.Value;
            if (string.IsNullOrWhiteSpace(options.TokenEndpoint))
            {
                throw new InvalidOperationException("ServiceAuth:TokenEndpoint is required.");
            }

            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new InvalidOperationException("ServiceAuth:ClientId and ServiceAuth:ClientSecret are required.");
            }

            var payload = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret
            };

            if (!string.IsNullOrWhiteSpace(options.Scope))
            {
                payload["scope"] = options.Scope!;
            }

            if (!string.IsNullOrWhiteSpace(options.Audience))
            {
                payload["audience"] = options.Audience!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(payload)
            };

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var (token, expiresIn) = await ReadTokenAsync(response, ct).ConfigureAwait(false);
            _cachedToken = token;
            _expiresAt = now.AddSeconds(expiresIn);

            return token;
        }
        finally
        {
            _sync.Release();
        }
    }

    private static async Task<(string AccessToken, int ExpiresIn)> ReadTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            throw new InvalidOperationException("Token response missing access_token.");
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token response access_token is empty.");
        }

        var expiresIn = 300;
        if (document.RootElement.TryGetProperty("expires_in", out var expiresElement))
        {
            if (expiresElement.ValueKind == JsonValueKind.Number && expiresElement.TryGetInt32(out var seconds))
            {
                expiresIn = seconds;
            }
            else if (expiresElement.ValueKind == JsonValueKind.String &&
                     int.TryParse(expiresElement.GetString(), out var parsed))
            {
                expiresIn = parsed;
            }
        }

        return (token, expiresIn);
    }

    public void Dispose()
    {
        _sync.Dispose();
    }
}

public sealed class ServiceAuthenticationHandler : DelegatingHandler
{
    private readonly IServiceTokenProvider _tokenProvider;

    public ServiceAuthenticationHandler(IServiceTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

public static class ServiceAuthenticationExtensions
{
    public static IServiceCollection AddServiceAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ServiceAuthenticationOptions>(configuration.GetSection("ServiceAuth"));
        services.AddHttpClient<IServiceTokenProvider, ServiceTokenProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<ServiceAuthenticationHandler>();
        return services;
    }

    public static IHttpClientBuilder AddServiceAuthentication(this IHttpClientBuilder builder)
        => builder.AddHttpMessageHandler<ServiceAuthenticationHandler>();
}
