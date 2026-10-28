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
    public void ReverseProxyShouldHave16Routes()
    {
        // 6 backend service routes (identity, servers, console, notifications, secrets, discord) + Better Auth + console hub route + agent hub route + nodes-org + enrollment + agents + agents-enroll + agents-ca + internal-block + nodes-reservations-block = 16 total (billing, tasks, files, mods are parked, D-03). servers and console are organization-scoped (VS-07R).
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren().ToList();

        Assert.Equal(16, routes.Count);
    }

    [Fact]
    public void AgentHubRoute_ShouldMatchHubPathWithCertificatePolicyAndNoTransforms()
    {
        var route = _configuration.GetSection("ReverseProxy:Routes:agent-hub-route");

        Assert.Equal("nodes", route.GetValue<string>("ClusterId"));
        Assert.Equal("/hubs/agent/{**catch-all}", route.GetValue<string>("Match:Path"));
        Assert.Equal("AgentCertificate", route.GetValue<string>("AuthorizationPolicy"));
        Assert.Empty(route.GetSection("Transforms").GetChildren());
        Assert.Equal("true", route.GetValue<string>("Metadata:ForwardClientCertificate"));
    }

    [Fact]
    public void AgentsRoute_ShouldRequireAgentCertificateAndForwardClientCertificate()
    {
        var route = _configuration.GetSection("ReverseProxy:Routes:agents-route");

        Assert.Equal("AgentCertificate", route.GetValue<string>("AuthorizationPolicy"));
        Assert.Equal("true", route.GetValue<string>("Metadata:ForwardClientCertificate"));
    }

    [Fact]
    public void AgentsEnrollRoute_ShouldStayAnonymousWithoutForwardingMetadata()
    {
        var route = _configuration.GetSection("ReverseProxy:Routes:agents-enroll-route");

        Assert.Equal("Anonymous", route.GetValue<string>("AuthorizationPolicy"));
        Assert.False(route.GetSection("Metadata").Exists());
    }

    [Fact]
    public void NodesCluster_ShouldHaveCookieSessionAffinity()
    {
        var affinity = _configuration.GetSection("ReverseProxy:Clusters:nodes:SessionAffinity");

        Assert.True(affinity.GetValue<bool>("Enabled"));
        Assert.Equal("Cookie", affinity.GetValue<string>("Policy"));
        Assert.Equal(".Dhadgar.Nodes.Affinity", affinity.GetValue<string>("AffinityKeyName"));
    }

    [Fact]
    public void AgentIngress_ShouldDefaultToNoCertificateMode()
    {
        var mode = _configuration["AgentIngress:ClientCertificateMode"];

        Assert.Equal("NoCertificate", mode);
    }

    [Fact]
    public void ReverseProxyShouldHave8Clusters()
    {
        // 8 active backend clusters (billing, tasks, files, mods are parked, D-03; agents uses nodes cluster)
        var clustersSection = _configuration.GetSection("ReverseProxy:Clusters");
        var clusters = clustersSection.GetChildren().ToList();

        Assert.Equal(8, clusters.Count);
    }

    [Theory]
    [InlineData("identity", "5010")]
    [InlineData("servers", "5030")]
    [InlineData("nodes", "5040")]
    [InlineData("console", "5070")]
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
    [InlineData("servers-org-route", "/api/v1/organizations/{organizationId}/servers/{**catch-all}")]
    [InlineData("console-org-route", "/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}")]
    [InlineData("console-hub-route", "/hubs/console/{**catch-all}")]
    [InlineData("agent-hub-route", "/hubs/agent/{**catch-all}")]
    [InlineData("agents-ca-route", "/api/v1/agents/ca-certificate")]
    [InlineData("nodes-reservations-block", "/api/v1/organizations/{organizationId}/nodes/{nodeId}/reservations/{**catch-all}")]
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
    public void NodesReservationsBlockRoute_ShouldBeDenyAllOnNodesWithoutTransforms()
    {
        var route = _configuration.GetSection("ReverseProxy:Routes:nodes-reservations-block");

        Assert.Equal("DenyAll", route.GetValue<string>("AuthorizationPolicy"));
        Assert.Equal("nodes", route.GetValue<string>("ClusterId"));
        Assert.Empty(route.GetSection("Transforms").GetChildren());
    }

    [Fact]
    public void AgentsCaRoute_ShouldBeAnonymousWithoutMetadata()
    {
        var route = _configuration.GetSection("ReverseProxy:Routes:agents-ca-route");

        Assert.Equal("Anonymous", route.GetValue<string>("AuthorizationPolicy"));
        Assert.Equal("nodes", route.GetValue<string>("ClusterId"));
        Assert.False(route.GetSection("Metadata").Exists());
    }

    [Theory]
    [InlineData("servers-org-route")]
    [InlineData("console-org-route")]
    public void OrgScopedRoutes_ShouldStripApiV1Prefix(string routeName)
    {
        var transforms = _configuration.GetSection($"ReverseProxy:Routes:{routeName}:Transforms")
            .GetChildren()
            .Select(t => t.GetValue<string>("PathRemovePrefix"))
            .ToList();

        Assert.Equal(new[] { "/api/v1" }, transforms);
    }

    [Theory]
    [InlineData("identity-internal-block", "identity-route")]
    [InlineData("nodes-reservations-block", "nodes-org-route")]
    public void BlockRoute_PrecedesTheRouteItShadows(string blockRoute, string shadowedRoute)
    {
        var blockOrder = _configuration.GetValue<int?>($"ReverseProxy:Routes:{blockRoute}:Order");
        var shadowedOrder = _configuration.GetValue<int?>($"ReverseProxy:Routes:{shadowedRoute}:Order");

        Assert.NotNull(blockOrder);
        Assert.NotNull(shadowedOrder);
        Assert.True(blockOrder < shadowedOrder, $"{blockRoute} ({blockOrder}) must precede {shadowedRoute} ({shadowedOrder})");
    }

    [Theory]
    [InlineData("identity-internal-block", 1)]
    [InlineData("identity-route", 10)]
    [InlineData("betterauth-route", 10)]
    [InlineData("servers-org-route", 20)]
    [InlineData("console-org-route", -1)]
    [InlineData("agent-hub-route", 20)]
    [InlineData("nodes-org-route", 20)]
    [InlineData("enrollment-route", 20)]
    [InlineData("nodes-reservations-block", 1)]
    [InlineData("agents-ca-route", 22)]
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

    /// <summary>
    /// Nodes maps its endpoints at the root (/agents/*, /organizations/{id}/nodes/*, ...)
    /// with no path base, so every Gateway route into the nodes cluster must strip /api/v1.
    /// Without this, agent enrollment and all node management 404 through the Gateway.
    /// </summary>
    [Theory]
    [InlineData("agents-enroll-route", "/api/v1/agents/enroll", "/agents/enroll")]
    [InlineData("agents-route", "/api/v1/agents/3f2a/heartbeat", "/agents/3f2a/heartbeat")]
    [InlineData("nodes-org-route", "/api/v1/organizations/abc/nodes/xyz", "/organizations/abc/nodes/xyz")]
    [InlineData("enrollment-route", "/api/v1/organizations/abc/enrollment/tokens", "/organizations/abc/enrollment/tokens")]
    public void NodesRoutes_ShouldForwardToNodesRootPaths(string routeName, string publicPath, string expectedDownstream)
    {
        var route = _configuration.GetSection($"ReverseProxy:Routes:{routeName}");
        Assert.Equal("nodes", route.GetValue<string>("ClusterId"));

        var prefix = route.GetSection("Transforms").GetChildren()
            .Select(t => t.GetValue<string>("PathRemovePrefix"))
            .SingleOrDefault(p => p is not null);

        Assert.NotNull(prefix);
        Assert.StartsWith(prefix, publicPath, StringComparison.Ordinal);
        Assert.Equal(expectedDownstream, publicPath[prefix.Length..]);
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
