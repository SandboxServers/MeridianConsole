using Refit;
using System.Text.Json.Serialization;

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
