using Refit;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Factory for creating Refit API clients with automatic Bearer token authentication
/// </summary>
public class ApiClientFactory
{
    private readonly string _gatewayUrl;
    private readonly string _identityUrl;
    private readonly string _secretsUrl;
    private readonly string? _accessToken;

    public ApiClientFactory(string gatewayUrl = "http://localhost:5000", 
                          string identityUrl = "http://localhost:5001",
                          string secretsUrl = "http://localhost:5002",
                          string? accessToken = null)
    {
        _gatewayUrl = gatewayUrl;
        _identityUrl = identityUrl;
        _secretsUrl = secretsUrl;
        _accessToken = accessToken;
    }

    public IIdentityApi CreateIdentityClient()
    {
        return RestService.For<IIdentityApi>(
            new HttpClient(new AuthenticatedHttpClientHandler(_accessToken))
            {
                BaseAddress = new Uri(_identityUrl)
            });
    }

    public ISecretsApi CreateSecretsClient()
    {
        return RestService.For<ISecretsApi>(
            new HttpClient(new AuthenticatedHttpClientHandler(_accessToken))
            {
                BaseAddress = new Uri(_secretsUrl)
            });
    }

    public IKeyVaultApi CreateKeyVaultClient()
    {
        return RestService.For<IKeyVaultApi>(
            new HttpClient(new AuthenticatedHttpClientHandler(_accessToken))
            {
                BaseAddress = new Uri(_secretsUrl)
            });
    }

    public IGatewayApi CreateGatewayClient()
    {
        return RestService.For<IGatewayApi>(
            new HttpClient(new AuthenticatedHttpClientHandler(_accessToken))
            {
                BaseAddress = new Uri(_gatewayUrl)
            });
    }

    /// <summary>
    /// HTTP handler that automatically adds Bearer token to requests
    /// </summary>
    private class AuthenticatedHttpClientHandler : HttpClientHandler
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
