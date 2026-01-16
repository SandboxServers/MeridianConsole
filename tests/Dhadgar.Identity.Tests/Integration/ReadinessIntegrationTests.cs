using System.Net;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

public sealed class ReadinessIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public ReadinessIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReadyzReturnsOkWhenRedisIsHealthy()
    {
        using var client = CreateClient(success: true);

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzReturnsServiceUnavailableWhenRedisIsDown()
    {
        using var client = CreateClient(success: false);

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private HttpClient CreateClient(bool success)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    var remaining = options.Registrations
                        .Where(registration => !string.Equals(registration.Name, "identity_ready", StringComparison.Ordinal))
                        .ToList();

                    options.Registrations.Clear();
                    foreach (var registration in remaining)
                    {
                        options.Registrations.Add(registration);
                    }

                    options.Registrations.Add(new HealthCheckRegistration(
                        "identity_ready",
                        new StubHealthCheck(success),
                        HealthStatus.Unhealthy,
                        tags: new[] { "ready" }));
                });
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private sealed class StubHealthCheck : IHealthCheck
    {
        private readonly bool _success;

        public StubHealthCheck(bool success)
        {
            _success = success;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
        {
            return Task.FromResult(_success
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("redis_unavailable"));
        }
    }
}
