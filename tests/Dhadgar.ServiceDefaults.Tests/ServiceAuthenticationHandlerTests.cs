using System.Net;
using System.Text;
using Dhadgar.ServiceDefaults;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests;

public sealed class ServiceAuthenticationHandlerTests
{
    [Fact]
    public async Task TokenProvider_caches_token_until_expiry()
    {
        using var handler = new TokenEndpointHandler();
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new ServiceAuthenticationOptions
        {
            TokenEndpoint = "https://identity.test/connect/token",
            ClientId = "client",
            ClientSecret = "secret"
        });

        using var provider = new ServiceTokenProvider(httpClient, options, TimeProvider.System);

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task AuthenticationHandler_adds_bearer_header()
    {
        var provider = new StubTokenProvider("service-token");
        using var recorder = new RecordingHandler();
        using var authHandler = new ServiceAuthenticationHandler(provider)
        {
            InnerHandler = recorder
        };

        using var client = new HttpClient(authHandler);
        var response = await client.GetAsync(new Uri("https://example.test/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", recorder.Authorization?.Scheme);
        Assert.Equal("service-token", recorder.Authorization?.Parameter);
    }

    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;

            var payload = new { access_token = $"token-{CallCount}", expires_in = 3600 };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubTokenProvider : IServiceTokenProvider
    {
        private readonly string _token;

        public StubTokenProvider(string token)
        {
            _token = token;
        }

        public ValueTask<string> GetAccessTokenAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_token);
    }
}
