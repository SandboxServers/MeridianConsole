using Dhadgar.Lab.Services;
using Xunit;

namespace Dhadgar.Lab.Tests.Services;

public sealed class ServiceAdapterTests
{
    private static LabServiceContext Context(string? nodesDb = "Host=meridian-lab-toxiproxy;Port=20020;Database=dhadgar_platform") =>
        new(
            "a9c623a1",
            nodesDb is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["nodes"] = nodesDb },
            "C:\\lab\\state\\nodes-ca",
            "lab_user",
            "lab-password-value");

    [Fact]
    public void Nodes_ReachesPostgresAndRabbitMq_ThroughToxiproxy()
    {
        var env = new NodesAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Contains("Port=20020", env.Value!["ConnectionStrings__Postgres"]);
        Assert.Equal("meridian-lab-toxiproxy", env.Value["RabbitMq__Host"]);
        Assert.Equal("20021", env.Value["RabbitMq__Port"]);
    }

    [Fact]
    public void Nodes_CaStorage_IsUnderLabState_NotDevCa()
    {
        var env = new NodesAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("/lab-state/nodes-ca", env.Value!["Nodes__CaStoragePath"]);
    }

    [Fact]
    public void Nodes_MissingConnectionString_IsInvalidArgument()
    {
        var env = new NodesAdapter().Environment(Context(nodesDb: null));

        Assert.True(env.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_ReachesNodes_ThroughGatewayNodesProxy_AndDisablesDynamicFetch()
    {
        var env = new GatewayAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("http://meridian-lab-toxiproxy:20012/", env.Value!["ReverseProxy__Clusters__nodes__Destinations__d1__Address"]);
        Assert.Equal("false", env.Value["Cloudflare__EnableDynamicFetch"]);
        Assert.Equal("nodes", env.Value["Readyz__RequiredClusters__0"]);
    }

    [Fact]
    public void Gateway_RedisUsesVaultBuiltString_WhenProvided()
    {
        var ctx = Context() with
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["gateway-redis"] = "meridian-lab-toxiproxy:20010,password=p,abortConnect=false",
            },
        };

        var env = new GatewayAdapter().Environment(ctx);

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("meridian-lab-toxiproxy:20010,password=p,abortConnect=false", env.Value!["Redis__ConnectionString"]);
    }

    [Fact]
    public void Overlay_KeysMapToDoubleUnderscoreEnvironmentNames()
    {
        var overlay = new Dictionary<string, string> { ["Readyz:RequiredClusters:0"] = "nodes" };

        var env = ConfigOverlay.ToEnvironment(overlay);

        Assert.Equal("nodes", env["Readyz__RequiredClusters__0"]);
        Assert.False(env.ContainsKey("Readyz:RequiredClusters:0"));
    }

    [Fact]
    public void Adapters_OverlayAllowlist_MatchesC4()
    {
        Assert.Equal(
            new[] { "Logging:LogLevel:Default", "Logging:LogLevel:Dhadgar", "Logging:LogLevel:Microsoft.AspNetCore", "Mtls:RenewalGraceMinutes" }
                .Order(StringComparer.Ordinal),
            new NodesAdapter().OverlayKeys.Order(StringComparer.Ordinal));
        Assert.Contains("Readyz:RequiredClusters:3", new GatewayAdapter().OverlayKeys);
    }

    [Fact]
    public void Adapters_SetOnlyAllowlistedOverlayKeys()
    {
        // Overlay keys are the only keys a config overlay may set (C4 allowlist); none is an adapter-owned key.
        foreach (ILabServiceAdapter adapter in new ILabServiceAdapter[] { new GatewayAdapter(), new NodesAdapter() })
        {
            Assert.DoesNotContain("Cloudflare__EnableDynamicFetch", adapter.OverlayKeys);
            Assert.DoesNotContain("ConnectionStrings__Postgres", adapter.OverlayKeys);
        }
    }
}
