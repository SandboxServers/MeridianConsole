using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class RouteConfigurationTests
{
    private readonly IConfiguration _configuration;

    public RouteConfigurationTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile(GetGatewayAppSettingsPath(), optional: false)
            .Build();
    }

    [Fact]
    public void ReverseProxyConfigurationShouldExist()
    {
        var reverseProxySection = _configuration.GetSection("ReverseProxy");
        Assert.NotNull(reverseProxySection);
    }

    [Fact]
    public void ReverseProxyShouldHave17Routes()
    {
        // 10 backend services + Better Auth + console hub route + nodes-org + enrollment + agents + agents-enroll + internal-block = 17 total
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren().ToList();

        Assert.Equal(17, routes.Count);
    }

    [Fact]
    public void ReverseProxyShouldHave12Clusters()
    {
        // 11 backend services + Better Auth (agents uses nodes cluster)
        var clustersSection = _configuration.GetSection("ReverseProxy:Clusters");
        var clusters = clustersSection.GetChildren().ToList();

        Assert.Equal(12, clusters.Count);
    }

    [Theory]
    [InlineData("identity", "5010")]
    [InlineData("billing", "5020")]
    [InlineData("servers", "5030")]
    [InlineData("nodes", "5040")]
    [InlineData("tasks", "5050")]
    [InlineData("files", "5060")]
    [InlineData("console", "5070")]
    [InlineData("mods", "5080")]
    [InlineData("notifications", "5090")]
    [InlineData("secrets", "5110")]
    [InlineData("discord", "5120")]
    [InlineData("betterauth", "5130")]
    public void ClusterShouldHaveCorrectPort(string clusterName, string expectedPort)
    {
        var address = _configuration[$"ReverseProxy:Clusters:{clusterName}:Destinations:d1:Address"];
        Assert.NotNull(address);
        Assert.Contains($":{expectedPort}", address, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identity-route", "/api/v1/identity/{**catch-all}")]
    [InlineData("betterauth-route", "/api/v1/betterauth/{**catch-all}")]
    [InlineData("servers-route", "/api/v1/servers/{**catch-all}")]
    [InlineData("console-hub-route", "/hubs/console/{**catch-all}")]
    public void RouteShouldHaveCorrectPathPattern(string routeName, string expectedPath)
    {
        var path = _configuration[$"ReverseProxy:Routes:{routeName}:Match:Path"];
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ConsoleClusterShouldHaveSessionAffinity()
    {
        var sessionAffinityEnabled = _configuration
            .GetValue<bool>("ReverseProxy:Clusters:console:SessionAffinity:Enabled");

        Assert.True(sessionAffinityEnabled);
    }

    [Fact]
    public void InternalBlockRouteShouldHaveDenyAllPolicy()
    {
        var policy = _configuration["ReverseProxy:Routes:identity-internal-block:AuthorizationPolicy"];
        var path = _configuration["ReverseProxy:Routes:identity-internal-block:Match:Path"];
        var order = _configuration.GetValue<int?>("ReverseProxy:Routes:identity-internal-block:Order");

        Assert.Equal("DenyAll", policy);
        Assert.Equal("/api/v1/identity/internal/{**catch-all}", path);
        Assert.Equal(1, order);
    }

    [Fact]
    public void InternalBlockRoute_ShouldHaveLowestOrder()
    {
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren()
            .Select(r => new
            {
                RouteId = r.Key,
                Order = r.GetValue<int?>("Order") ?? 999
            })
            .OrderBy(r => r.Order)
            .ToList();

        // Internal block route should be first (Order=1)
        Assert.Equal("identity-internal-block", routes.First().RouteId);
    }

    [Theory]
    [InlineData("identity-internal-block", 1)]
    [InlineData("identity-route", 10)]
    [InlineData("betterauth-route", 10)]
    [InlineData("servers-route", 20)]
    [InlineData("nodes-org-route", 20)]
    [InlineData("enrollment-route", 20)]
    [InlineData("agents-enroll-route", 25)]
    [InlineData("agents-route", 30)]
    public void RouteShouldHaveCorrectOrder(string routeName, int expectedOrder)
    {
        var order = _configuration.GetValue<int?>($"ReverseProxy:Routes:{routeName}:Order");
        Assert.Equal(expectedOrder, order);
    }

    [Fact]
    public void AllRoutesShouldHaveExplicitOrder()
    {
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routesWithoutOrder = routesSection.GetChildren()
            .Where(r => r.GetValue<int?>("Order") == null)
            .Select(r => r.Key)
            .ToList();

        Assert.Empty(routesWithoutOrder);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("servers")]
    [InlineData("nodes")]
    [InlineData("console")]
    public void ClusterShouldHaveActiveHealthCheck(string clusterName)
    {
        var healthCheckEnabled = _configuration
            .GetValue<bool>($"ReverseProxy:Clusters:{clusterName}:HealthCheck:Active:Enabled");

        Assert.True(healthCheckEnabled);
    }

    private static string GetGatewayAppSettingsPath()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "src", "Dhadgar.Gateway", "appsettings.json");
    }

    private static string FindSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Could not locate Dhadgar.sln to load gateway configuration.");
        }

        return directory.FullName;
    }
}
