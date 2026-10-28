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
