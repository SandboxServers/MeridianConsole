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
    private static readonly Uri DefaultNotificationsUri = new("http://localhost:5008");
    private static readonly Uri DefaultDiscordUri = new("http://localhost:5009");

    private readonly Uri _gatewayUri;
    private readonly Uri _identityUri;
    private readonly Uri _secretsUri;
    private readonly Uri _notificationsUri;
    private readonly Uri _discordUri;
    private readonly string? _accessToken;
    private readonly AuthenticatedHttpClientHandler _identityHandler;
    private readonly AuthenticatedHttpClientHandler _secretsHandler;
    private readonly AuthenticatedHttpClientHandler _keyVaultHandler;
    private readonly AuthenticatedHttpClientHandler _gatewayHandler;
    private readonly AuthenticatedHttpClientHandler _notificationsHandler;
    private readonly AuthenticatedHttpClientHandler _discordHandler;
    private readonly HttpClient _identityClient;
    private readonly HttpClient _secretsClient;
    private readonly HttpClient _keyVaultClient;
    private readonly HttpClient _gatewayClient;
    private readonly HttpClient _notificationsClient;
    private readonly HttpClient _discordClient;

    public ApiClientFactory(
        Uri? gatewayUrl = null,
        Uri? identityUrl = null,
        Uri? secretsUrl = null,
        Uri? notificationsUrl = null,
        Uri? discordUrl = null,
        string? accessToken = null)
    {
        _gatewayUri = gatewayUrl ?? DefaultGatewayUri;
        _identityUri = identityUrl ?? DefaultIdentityUri;
        _secretsUri = secretsUrl ?? DefaultSecretsUri;
        _notificationsUri = notificationsUrl ?? DefaultNotificationsUri;
        _discordUri = discordUrl ?? DefaultDiscordUri;
        _accessToken = accessToken;

        _identityHandler = CreateHandler();
        _secretsHandler = CreateHandler();
        _keyVaultHandler = CreateHandler();
        _gatewayHandler = CreateHandler();
        _notificationsHandler = CreateHandler();
        _discordHandler = CreateHandler();

        _identityClient = CreateClient(_identityUri, _identityHandler);
        _secretsClient = CreateClient(_secretsUri, _secretsHandler);
        _keyVaultClient = CreateClient(_secretsUri, _keyVaultHandler);
        _gatewayClient = CreateClient(_gatewayUri, _gatewayHandler);
        _notificationsClient = CreateClient(_notificationsUri, _notificationsHandler);
        _discordClient = CreateClient(_discordUri, _discordHandler);
    }

    public ApiClientFactory(CliConfig config)
        : this(
            gatewayUrl: EnsureAbsoluteUri(config.EffectiveGatewayUrl, "Gateway URL"),
            identityUrl: EnsureAbsoluteUri(config.EffectiveIdentityUrl, "Identity URL"),
            secretsUrl: NormalizeSecretsBase(EnsureAbsoluteUri(config.SecretsUrl ?? config.EffectiveGatewayUrl, "Secrets URL")),
            notificationsUrl: EnsureAbsoluteUri(config.EffectiveNotificationsUrl, "Notifications URL"),
            discordUrl: EnsureAbsoluteUri(config.EffectiveDiscordUrl, "Discord URL"),
            accessToken: config.AccessToken)
    {
    }

    public static ApiClientFactory? TryCreate(
        CliConfig config,
        out string error)
    {
        error = string.Empty;

        if (!TryResolveUri(null, config.EffectiveGatewayUrl, "Gateway URL", out var gatewayUri, out error))
        {
            return null;
        }

        if (!TryResolveUri(null, config.EffectiveIdentityUrl, "Identity URL", out var identityUri, out error))
        {
            return null;
        }

        if (!TryResolveSecretsUri(null, config.SecretsUrl ?? config.EffectiveGatewayUrl, out var secretsUri, out error))
        {
            return null;
        }

        if (!TryResolveUri(null, config.EffectiveNotificationsUrl, "Notifications URL", out var notificationsUri, out error))
        {
            return null;
        }

        if (!TryResolveUri(null, config.EffectiveDiscordUrl, "Discord URL", out var discordUri, out error))
        {
            return null;
        }

        return new ApiClientFactory(gatewayUri, identityUri, secretsUri, notificationsUri, discordUri, config.AccessToken);
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

        if (!TryResolveUri(null, config.EffectiveNotificationsUrl, "Notifications URL", out var notificationsUri, out error))
        {
            return null;
        }

        if (!TryResolveUri(null, config.EffectiveDiscordUrl, "Discord URL", out var discordUri, out error))
        {
            return null;
        }

        return new ApiClientFactory(gatewayUri, identityUri, secretsUri, notificationsUri, discordUri, config.AccessToken);
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

    public INotificationsApi CreateNotificationsClient()
    {
        return RestService.For<INotificationsApi>(_notificationsClient);
    }

    public IDiscordApi CreateDiscordClient()
    {
        return RestService.For<IDiscordApi>(_discordClient);
    }

    public IHealthApi CreateNotificationsHealthClient()
    {
        return RestService.For<IHealthApi>(_notificationsClient);
    }

    public IHealthApi CreateDiscordHealthClient()
    {
        return RestService.For<IHealthApi>(_discordClient);
    }

    public void Dispose()
    {
        _identityClient.Dispose();
        _secretsClient.Dispose();
        _keyVaultClient.Dispose();
        _gatewayClient.Dispose();
        _notificationsClient.Dispose();
        _discordClient.Dispose();
        _identityHandler.Dispose();
        _secretsHandler.Dispose();
        _keyVaultHandler.Dispose();
        _gatewayHandler.Dispose();
        _notificationsHandler.Dispose();
        _discordHandler.Dispose();
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
