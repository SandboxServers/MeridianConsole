using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Nodes.Tests.Lab;

/// <summary>
/// L52: the Nodes routes the host maps must equal the <c>nodes.routes</c> of the lab's coverage universe. The universe is
/// read as a file from the repo root; this test adds no product code and no project reference to the lab. When a route is
/// added to Nodes, update <c>src/Tools/Dhadgar.Lab/coverage-universe.json</c>, so "unhit" never silently shrinks.
/// </summary>
public sealed class NodesEndpointUniverseTests
{
    private const string UniverseFile = "src/Tools/Dhadgar.Lab/coverage-universe.json";

    // The Gateway strips /api/v1 for these prefixes (appsettings.json routes); the hub is mapped without it.
    private const string PublicPrefix = "/api/v1";
    private const string HubPrefix = "/hubs/";

    // The routes the Nodes endpoint files map. Health and service-info endpoints are not Nodes routes.
    private static readonly string[] NodesRoutePrefixes = ["/agents", "/organizations/", "/reservations/", "/hubs/"];

    private static readonly Regex ConstraintPattern = new(
        @"\{([A-Za-z_][A-Za-z0-9_]*)(:[^}]*)?\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void MappedNodesRoutes_EqualTheCoverageUniverse()
    {
        using var factory = new NodesWebApplicationFactory();
        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();

        var mapped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (RouteEndpoint endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            string template = NormalizeTemplate(endpoint.RoutePattern.RawText ?? string.Empty);
            if (!NodesRoutePrefixes.Any(prefix => template.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            if (template.StartsWith(HubPrefix, StringComparison.Ordinal))
            {
                mapped.Add("* " + template);
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (string method in methods)
            {
                mapped.Add(method.ToUpperInvariant() + " " + PublicPrefix + template);
            }
        }

        SortedSet<string> universe = UniverseRoutes();

        var missing = mapped.Except(universe, StringComparer.Ordinal).ToList();
        var extra = universe.Except(mapped, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            "Nodes routes differ from the coverage universe. Missing from the universe: ["
            + string.Join(", ", missing) + "]. Extra in the universe: [" + string.Join(", ", extra)
            + "]. Update src/Tools/Dhadgar.Lab/coverage-universe.json.");
    }

    /// <summary>
    /// Removes the route constraints (<c>{nodeId:guid}</c> becomes <c>{nodeId}</c>) and the trailing slash an empty group
    /// route leaves, the form the universe uses.
    /// </summary>
    private static string NormalizeTemplate(string rawText)
    {
        string withoutConstraints = ConstraintPattern.Replace(rawText, match => "{" + match.Groups[1].Value + "}");
        return withoutConstraints.Length > 1 ? withoutConstraints.TrimEnd('/') : withoutConstraints;
    }

    /// <summary>
    /// The <c>nodes.routes</c> of the shipped universe as <c>METHOD public-path</c> strings.
    /// </summary>
    private static SortedSet<string> UniverseRoutes()
    {
        string path = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), UniverseFile.Replace('/', Path.DirectorySeparatorChar));
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);

        var routes = new SortedSet<string>(StringComparer.Ordinal);
        JsonElement nodesRoutes = document.RootElement.GetProperty("services").GetProperty("nodes").GetProperty("routes");
        foreach (JsonElement route in nodesRoutes.EnumerateArray())
        {
            routes.Add(route.GetProperty("method").GetString() + " " + route.GetProperty("template").GetString());
        }

        return routes;
    }

    /// <summary>
    /// Walks up from <paramref name="start"/> to the folder that holds <c>Dhadgar.sln</c>.
    /// </summary>
    private static string FindRepoRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Dhadgar.sln was not found above the test output folder.");
    }
}
