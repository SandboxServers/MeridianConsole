using System.Text.Json;
using Dhadgar.Lab.Invariants;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// The lab's route table must match the Gateway's YARP routes, so a new Gateway route fails here until the table is updated.
/// </summary>
public sealed class GatewayRouteTableTests
{
    [Fact]
    public void EveryGatewayRoute_IsInTheTable_WithTheSameCluster()
    {
        string path = Path.Combine(FindRepoRoot(), "src", "Dhadgar.Gateway", "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        int count = 0;
        foreach (JsonProperty route in routes.EnumerateObject())
        {
            count++;
            string template = route.Value.GetProperty("Match").GetProperty("Path").GetString()!;
            string cluster = route.Value.GetProperty("ClusterId").GetString()!;
            int brace = template.IndexOf('{', StringComparison.Ordinal);
            string literal = brace >= 0 ? template[..brace] : template;

            Assert.Contains(GatewayRouteTable.Routes, entry => entry.PathPrefix == literal && entry.ClusterId == cluster);
            Assert.Equal(cluster, GatewayRouteTable.ClusterFor(literal + "sample"));
        }

        Assert.True(count > 0);
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
