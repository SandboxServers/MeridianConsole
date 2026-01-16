using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class GatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new[]
            {
                new KeyValuePair<string, string?>("Cors:AllowedOrigins:0", "https://panel.meridianconsole.com")
            };
            config.AddInMemoryCollection(settings);
        });
    }
}

public class GatewayIntegrationTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly HttpClient _client;

    public GatewayIntegrationTests(GatewayWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LivezEndpointReturnsOk()
    {
        var response = await _client.GetAsync("/livez");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzEndpointReturnsOk()
    {
        var response = await _client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResponseIncludesCorrelationHeaders()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.True(response.Headers.Contains("X-Request-Id"));
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }

    [Fact]
    public async Task ResponseEchoesProvidedCorrelationId()
    {
        var expectedCorrelationId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("X-Correlation-Id", expectedCorrelationId);

        var response = await _client.SendAsync(request);
        var returnedCorrelationId = response.Headers.GetValues("X-Correlation-Id").First();

        Assert.Equal(expectedCorrelationId, returnedCorrelationId);
    }

    [Fact]
    public async Task ResponseIncludesSecurityHeaders()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("X-XSS-Protection"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()",
            response.Headers.GetValues("Permissions-Policy").Single());
    }
}
