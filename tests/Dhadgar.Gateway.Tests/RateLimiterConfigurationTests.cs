using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class RateLimiterConfigurationTests
{
    private readonly IConfiguration _config;

    public RateLimiterConfigurationTests()
    {
        _config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
    }

    [Theory]
    [InlineData("betterauth-route", "Auth")]
    [InlineData("identity-route", "Auth")]
    [InlineData("billing-route", "PerTenant")]
    [InlineData("servers-route", "PerTenant")]
    [InlineData("nodes-route", "PerTenant")]
    [InlineData("tasks-route", "PerTenant")]
    [InlineData("files-route", "PerTenant")]
    [InlineData("console-api-route", "PerTenant")]
    [InlineData("console-hub-route", "PerTenant")]
    [InlineData("mods-route", "PerTenant")]
    [InlineData("notifications-route", "PerTenant")]
    [InlineData("secrets-route", "PerTenant")]
    [InlineData("discord-route", "PerTenant")]
    [InlineData("agents-route", "PerAgent")]
    public void Routes_ShouldDeclareRateLimiterPolicy(string routeName, string expectedPolicy)
    {
        var policy = _config[$"ReverseProxy:Routes:{routeName}:RateLimiterPolicy"];
        Assert.Equal(expectedPolicy, policy);
    }

    [Fact]
    public void GlobalRateLimit_Configured()
    {
        var limit = _config.GetValue<int?>("RateLimiting:Policies:Global:PermitLimit");
        var window = _config.GetValue<int?>("RateLimiting:Policies:Global:WindowSeconds");

        Assert.True(limit.HasValue && limit.Value > 0);
        Assert.True(window.HasValue && window.Value > 0);
    }

    [Fact]
    public void AuthRateLimit_Configured()
    {
        var limit = _config.GetValue<int?>("RateLimiting:Policies:Auth:PermitLimit");
        var window = _config.GetValue<int?>("RateLimiting:Policies:Auth:WindowSeconds");

        Assert.True(limit.HasValue && limit.Value > 0);
        Assert.True(window.HasValue && window.Value > 0);
    }
}
