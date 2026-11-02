using System.Text.Json;
using Dhadgar.Lab.Invariants;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// The lab's route table must match the Gateway's YARP routes (id, Match:Path template, cluster and Order), so a new, removed or changed
/// Gateway route fails here until the table is updated.
/// </summary>
public sealed class GatewayRouteTableTests
{
    [Fact]
    public void EveryGatewayRoute_IsInTheTable_WithTheSameTemplateClusterAndOrder()
    {
        string path = Path.Combine(FindRepoRoot(), "src", "Dhadgar.Gateway", "appsettings.json");

        // The .NET JSON configuration provider accepts comments and trailing commas, so the parse must too.
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), options);
        JsonElement routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        int count = 0;
        foreach (JsonProperty route in routes.EnumerateObject())
        {
            count++;
            string id = route.Name;

            int matchCount = 0;
            string? matchName = null;
            foreach (JsonProperty match in route.Value.GetProperty("Match").EnumerateObject())
            {
                matchCount++;
                matchName = match.Name;
            }

            Assert.True(
                matchCount == 1 && matchName == "Path",
                $"Gateway route {id} matches on more than Path; GatewayRouteTable models Path only.");

            string template = route.Value.GetProperty("Match").GetProperty("Path").GetString()!;
            string cluster = route.Value.GetProperty("ClusterId").GetString()!;
            int order = route.Value.TryGetProperty("Order", out JsonElement o) ? o.GetInt32() : 0;

            int found = 0;
            foreach (GatewayRoute entry in GatewayRouteTable.Routes)
            {
                if (entry.RouteId != id)
                {
                    continue;
                }

                found++;
                Assert.Equal(template, entry.Template);
                Assert.Equal(cluster, entry.ClusterId);
                Assert.Equal(order, entry.Order);
            }

            Assert.Equal(1, found);
        }

        Assert.True(count > 0);
        Assert.Equal(count, GatewayRouteTable.Routes.Count);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GatewayRoute entry in GatewayRouteTable.Routes)
        {
            ids.Add(entry.RouteId);
        }

        Assert.Equal(GatewayRouteTable.Routes.Count, ids.Count);
    }

    [Theory]
    [InlineData("/api/v1/organizations/o1/servers/s1/console/attach", "console")]
    [InlineData("/api/v1/organizations/o1/servers/s1/console", "console")]
    [InlineData("/api/v1/organizations/o1/servers/s1/start", "servers")]
    [InlineData("/api/v1/organizations/o1/servers", "servers")]
    [InlineData("/api/v1/organizations/o1/nodes/n1/reservations/r1", "nodes")]
    [InlineData("/api/v1/organizations/o1/nodes/n1", "nodes")]
    [InlineData("/api/v1/organizations/o1/enrollment/tokens", "nodes")]
    [InlineData("/api/v1/organizations/o1/audit-logs", null)]
    [InlineData("/api/v1/organizations", null)]
    [InlineData("/api/v1/servers/s1", null)]
    [InlineData("/api/v1/console/s1", null)]
    [InlineData("/api/v1/identity/internal/users", "identity")]
    [InlineData("/api/v1/betterauth/sign-in", "betterauth")]
    [InlineData("/hubs/console/negotiate?negotiateVersion=1", "console")]
    [InlineData("/hubs/agent/negotiate", "nodes")]
    [InlineData("/api/v1/agents/ca-certificate", "nodes")]
    [InlineData("/api/v1/agents/enroll", "nodes")]
    [InlineData("/api/v1/agents/a1/heartbeat", "nodes")]
    [InlineData("/API/V1/SECRETS/s1", "secrets")]
    public void ClusterFor_RoutesLikeTheGateway(string path, string? cluster)
    {
        Assert.Equal(cluster, GatewayRouteTable.ClusterFor(path));
    }

    [Fact]
    public void GatewayImpactSet_OrgScopedServersPath_IsServersNotNodes()
    {
        IReadOnlySet<string> impact = FaultImpactMap.ImpactSet("gateway", "/api/v1/organizations/o1/servers/s1/start");

        Assert.Contains("d:servers", impact);
        Assert.Contains("c:servers", impact);
        Assert.DoesNotContain("d:nodes", impact);
    }

    [Fact]
    public void UnroutedPath_HasNoCluster()
    {
        Assert.Null(GatewayRouteTable.ClusterFor("/api/v1/unrouted/thing"));
        Assert.Null(GatewayRouteTable.ClusterFor(null));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "Dhadgar.Gateway")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("repository root not found above the test binaries");
    }
}
