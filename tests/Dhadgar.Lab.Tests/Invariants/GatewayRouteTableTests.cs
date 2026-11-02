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
        JsonDocumentOptions options = new() { CommentHandling = JsonCommentHandling.Skip };
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), options);
        JsonElement routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        int count = 0;
        foreach (JsonProperty route in routes.EnumerateObject())
        {
            count++;
            string template = route.Value.GetProperty("Match").GetProperty("Path").GetString()!;
            string cluster = route.Value.GetProperty("ClusterId").GetString()!;

            Assert.Contains(GatewayRouteTable.Routes, entry => entry.Template == template && entry.ClusterId == cluster);
            Assert.Equal(cluster, GatewayRouteTable.ClusterFor(SamplePath(template)));
        }

        Assert.True(count > 0);
    }

    [Fact]
    public void UnroutedPath_HasNoCluster()
    {
        Assert.Null(GatewayRouteTable.ClusterFor("/api/v1/unrouted/thing"));
        Assert.Null(GatewayRouteTable.ClusterFor(null));
    }

    /// <summary>
    /// A concrete path for a route template: each parameter becomes a value, and a catch-all becomes a tail segment.
    /// </summary>
    private static string SamplePath(string template)
    {
        string[] parts = template.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("{**", StringComparison.Ordinal))
            {
                parts[i] = "tail";
            }
            else if (parts[i].StartsWith('{'))
            {
                parts[i] = "sample";
            }
        }

        return string.Join('/', parts);
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
