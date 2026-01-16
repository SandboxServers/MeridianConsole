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
    public void ReverseProxyShouldHave15Routes()
    {
        // 12 backend services + Better Auth + console hub route + agents route = 15 total
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren().ToList();

        Assert.Equal(15, routes.Count);
    }

    [Fact]
    public void ReverseProxyShouldHave13Clusters()
    {
        // 12 backend services + Better Auth (agents uses nodes cluster)
        var clustersSection = _configuration.GetSection("ReverseProxy:Clusters");
        var clusters = clustersSection.GetChildren().ToList();

        Assert.Equal(13, clusters.Count);
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
    [InlineData("firewall", "5100")]
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
