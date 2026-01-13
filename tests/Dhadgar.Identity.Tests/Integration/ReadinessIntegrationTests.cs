using System.Net;
using Dhadgar.Identity.Readiness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        using var client = CreateClient(new StubRedisReadinessProbe(success: true));

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzReturnsServiceUnavailableWhenRedisIsDown()
    {
        using var client = CreateClient(new StubRedisReadinessProbe(success: false));

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private HttpClient CreateClient(IRedisReadinessProbe probe)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRedisReadinessProbe>();
                services.AddSingleton(probe);
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private sealed class StubRedisReadinessProbe : IRedisReadinessProbe
    {
        private readonly bool _success;

        public StubRedisReadinessProbe(bool success)
        {
            _success = success;
        }

        public Task<RedisReadinessResult> CheckAsync(CancellationToken ct)
        {
            return Task.FromResult(_success
                ? RedisReadinessResult.Ready(TimeSpan.FromMilliseconds(1))
                : RedisReadinessResult.NotReady("redis_unavailable"));
        }
    }
}
