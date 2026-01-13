using Dhadgar.Cli.Configuration;
using Refit;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Factory for creating Refit API clients with automatic Bearer token authentication
/// </summary>
public sealed class ApiClientFactory : IDisposable
{
    private static readonly Uri DefaultGatewayUri = new("http://localhost:5000");
    private static readonly Uri DefaultIdentityUri = new("http://localhost:5001");
    private static readonly Uri DefaultSecretsUri = new("http://localhost:5002");

    private readonly Uri _gatewayUri;
    private readonly Uri _identityUri;
    private readonly Uri _secretsUri;
    private readonly string? _accessToken;
    private readonly AuthenticatedHttpClientHandler _identityHandler;
    private readonly AuthenticatedHttpClientHandler _secretsHandler;
    private readonly AuthenticatedHttpClientHandler _keyVaultHandler;
    private readonly AuthenticatedHttpClientHandler _gatewayHandler;
    private readonly HttpClient _identityClient;
    private readonly HttpClient _secretsClient;
    private readonly HttpClient _keyVaultClient;
    private readonly HttpClient _gatewayClient;

    public ApiClientFactory(
        Uri? gatewayUrl = null,
        Uri? identityUrl = null,
        Uri? secretsUrl = null,
        string? accessToken = null)
    {
        _gatewayUri = gatewayUrl ?? DefaultGatewayUri;
        _identityUri = identityUrl ?? DefaultIdentityUri;
        _secretsUri = secretsUrl ?? DefaultSecretsUri;
        _accessToken = accessToken;

        _identityHandler = CreateHandler();
        _secretsHandler = CreateHandler();
        _keyVaultHandler = CreateHandler();
        _gatewayHandler = CreateHandler();

        _identityClient = CreateClient(_identityUri, _identityHandler);
        _secretsClient = CreateClient(_secretsUri, _secretsHandler);
        _keyVaultClient = CreateClient(_secretsUri, _keyVaultHandler);
        _gatewayClient = CreateClient(_gatewayUri, _gatewayHandler);
    }

    public ApiClientFactory(CliConfig config)
        : this(
            gatewayUrl: EnsureAbsoluteUri(config.EffectiveGatewayUrl, "Gateway URL"),
            identityUrl: EnsureAbsoluteUri(config.EffectiveIdentityUrl, "Identity URL"),
            secretsUrl: NormalizeSecretsBase(EnsureAbsoluteUri(config.SecretsUrl ?? config.EffectiveGatewayUrl, "Secrets URL")),
            accessToken: config.AccessToken)
    {
    }

    public static ApiClientFactory? TryCreate(
        CliConfig config,
        out string error)
    {
        return TryCreate(config, null, null, null, out error);
    }

    public static ApiClientFactory? TryCreate(
        CliConfig config,
        Uri? gatewayUrlOverride,
        Uri? identityUrlOverride,
        Uri? secretsUrlOverride,
        out string error)
    {
        error = string.Empty;

        if (!TryResolveUri(gatewayUrlOverride, config.EffectiveGatewayUrl, "Gateway URL", out var gatewayUri, out error))
        {
            return null;
        }

        if (!TryResolveUri(identityUrlOverride, config.EffectiveIdentityUrl, "Identity URL", out var identityUri, out error))
        {
            return null;
        }

        if (!TryResolveSecretsUri(secretsUrlOverride, config.SecretsUrl ?? config.EffectiveGatewayUrl, out var secretsUri, out error))
        {
            return null;
        }

        return new ApiClientFactory(gatewayUri, identityUri, secretsUri, config.AccessToken);
    }

    public IIdentityApi CreateIdentityClient()
    {
        return RestService.For<IIdentityApi>(_identityClient);
    }

    public ISecretsApi CreateSecretsClient()
    {
        return RestService.For<ISecretsApi>(_secretsClient);
    }

    public IKeyVaultApi CreateKeyVaultClient()
    {
        return RestService.For<IKeyVaultApi>(_keyVaultClient);
    }

    public IGatewayApi CreateGatewayClient()
    {
        return RestService.For<IGatewayApi>(_gatewayClient);
    }

    public IHealthApi CreateGatewayHealthClient()
    {
        return RestService.For<IHealthApi>(_gatewayClient);
    }

    public IHealthApi CreateIdentityHealthClient()
    {
        return RestService.For<IHealthApi>(_identityClient);
    }

    public IHealthApi CreateSecretsHealthClient()
    {
        return RestService.For<IHealthApi>(_secretsClient);
    }

    public void Dispose()
    {
        _identityClient.Dispose();
        _secretsClient.Dispose();
        _keyVaultClient.Dispose();
        _gatewayClient.Dispose();
        _identityHandler.Dispose();
        _secretsHandler.Dispose();
        _keyVaultHandler.Dispose();
        _gatewayHandler.Dispose();
    }

    private AuthenticatedHttpClientHandler CreateHandler()
    {
        return new AuthenticatedHttpClientHandler(_accessToken)
        {
            CheckCertificateRevocationList = true
        };
    }

    private static HttpClient CreateClient(Uri baseAddress, AuthenticatedHttpClientHandler handler)
    {
        return new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress
        };
    }

    private static Uri NormalizeSecretsBase(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        const string suffix = "/api/v1/secrets";

        if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^suffix.Length];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            var builder = new UriBuilder(uri)
            {
                Path = path
            };
            return builder.Uri;
        }

        return uri;
    }

    private static bool TryResolveUri(
        Uri? overrideUri,
        string rawUrl,
        string label,
        out Uri uri,
        out string error)
    {
        if (overrideUri is not null)
        {
            if (!overrideUri.IsAbsoluteUri)
            {
                error = $"Invalid {label}: '{overrideUri}'";
                uri = null!;
                return false;
            }

            uri = overrideUri!;
            error = string.Empty;
            return true;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) || parsed is null)
        {
            error = $"Invalid {label}: '{rawUrl}'";
            uri = null!;
            return false;
        }

        uri = parsed;
        error = string.Empty;
        return true;
    }

    private static bool TryResolveSecretsUri(
        Uri? overrideUri,
        string rawUrl,
        out Uri uri,
        out string error)
    {
        if (overrideUri is not null)
        {
            if (!overrideUri.IsAbsoluteUri)
            {
                error = $"Invalid Secrets URL: '{overrideUri}'";
                uri = null!;
                return false;
            }

            uri = NormalizeSecretsBase(overrideUri!);
            error = string.Empty;
            return true;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) || parsed is null)
        {
            error = $"Invalid Secrets URL: '{rawUrl}'";
            uri = null!;
            return false;
        }

        uri = NormalizeSecretsBase(parsed);
        error = string.Empty;
        return true;
    }

    private static Uri EnsureAbsoluteUri(string rawUrl, string label)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid {label}: '{rawUrl}'");
        }

        return uri;
    }

    /// <summary>
    /// HTTP handler that automatically adds Bearer token to requests
    /// </summary>
    private sealed class AuthenticatedHttpClientHandler : HttpClientHandler
    {
        private readonly string? _accessToken;

        public AuthenticatedHttpClientHandler(string? accessToken = null)
        {
            _accessToken = accessToken;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, 
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_accessToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
