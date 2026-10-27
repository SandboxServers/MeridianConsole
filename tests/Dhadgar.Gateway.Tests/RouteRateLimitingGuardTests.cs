using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Guards the route config: a non-DenyAll route with no per-route limiter would fall back to the single
/// <c>Global</c> bucket, so every such route must name one of the three partitioned policies.
/// </summary>
[Collection("Gateway Integration")]
public class RouteRateLimitingGuardTests
{
    private static readonly HashSet<string> PartitionedPolicies = new(StringComparer.Ordinal) { "Auth", "PerTenant", "PerAgent" };

    private readonly GatewayWebApplicationFactory _factory;

    public RouteRateLimitingGuardTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void EveryNonDenyAllRoute_HasPartitionedRateLimiterPolicy()
    {
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var routes = configuration.GetSection("ReverseProxy:Routes").GetChildren();

        var unguarded = new List<string>();
        var checkedCount = 0;
        foreach (var route in routes)
        {
            var authorizationPolicy = route.GetValue<string>("AuthorizationPolicy");
            if (string.Equals(authorizationPolicy, "DenyAll", StringComparison.Ordinal))
            {
                continue;
            }

            checkedCount++;
            var rateLimiterPolicy = route.GetValue<string>("RateLimiterPolicy");
            if (rateLimiterPolicy is null || !PartitionedPolicies.Contains(rateLimiterPolicy))
            {
                unguarded.Add(route.Key);
            }
        }

        Assert.True(checkedCount > 0, "Expected at least one non-DenyAll route in appsettings.json.");
        Assert.Empty(unguarded);
    }
}
