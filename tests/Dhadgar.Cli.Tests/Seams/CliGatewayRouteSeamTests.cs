using System.Net;
using System.Text.Json;
using Dhadgar.Cli.Configuration;
using Dhadgar.Cli.Infrastructure.Clients;
using FluentAssertions;
using Refit;
using Xunit;

namespace Dhadgar.Cli.Tests.Seams;

/// <summary>
/// Checks that the CLI's default URLs and Refit paths route through the real Gateway.
/// Reads only <c>src/Dhadgar.Gateway/appsettings.json</c>; routes added by code in the Gateway are not seen.
/// Matching follows YARP for the cases used here: literal segments match exactly, <c>{name}</c> matches one
/// non-empty segment, <c>{**name}</c> matches the rest (possibly nothing), and the route with the most literal
/// segments wins, then the lower <c>Order</c>.
/// </summary>
public sealed class CliGatewayRouteSeamTests
{
    private const string OrgId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void TokenRequest_DefaultConfig_RoutesToIdentityConnectToken()
    {
        var requestPath = PathOf(new CliConfig().EffectiveIdentityUrl + RefitPath(typeof(IIdentityApi), nameof(IIdentityApi.GetTokenAsync)));

        var route = GatewayRoutes.Resolve(requestPath);

        route.Should().NotBeNull($"no Gateway route matches {requestPath}");
        route!.ClusterId.Should().Be("identity");
        route.ForwardedPath.Should().Be("/connect/token");
    }

    [Fact]
    public void NodesList_DefaultConfig_RoutesToNodesOrganizationNodes()
    {
        var listPath = RefitPath(typeof(INodesApi), nameof(INodesApi.GetNodesAsync)).Replace("{orgId}", OrgId);
        var requestPath = PathOf(new CliConfig().EffectiveNodesUrl + listPath);

        var route = GatewayRoutes.Resolve(requestPath);

        route.Should().NotBeNull($"no Gateway route matches {requestPath}");
        route!.ClusterId.Should().Be("nodes");
        route.ForwardedPath.Should().Be($"/organizations/{OrgId}/nodes");
    }

    [Fact]
    public async Task TokenRequest_ThroughRefit_SendsBasePathPlusConnectToken()
    {
        using var capture = new CaptureHandler();
        using var http = new HttpClient(capture) { BaseAddress = new Uri(new CliConfig().EffectiveIdentityUrl) };

        try
        {
            await RestService.For<IIdentityApi>(http)
                .GetTokenAsync(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }, CancellationToken.None);
        }
        catch (ApiException)
        {
            // The stub answers 400; only the request URI matters here.
        }

        capture.RequestUri.Should().NotBeNull();
        capture.RequestUri!.AbsolutePath.Should().Be("/api/v1/identity/connect/token");
        GatewayRoutes.Resolve(capture.RequestUri.AbsolutePath)!.ForwardedPath.Should().Be("/connect/token");
    }

    [Fact]
    public async Task NodesList_ThroughRefit_SendsBasePathPlusOrganizationNodes()
    {
        using var capture = new CaptureHandler();
        using var http = new HttpClient(capture) { BaseAddress = new Uri(new CliConfig().EffectiveNodesUrl) };

        try
        {
            await RestService.For<INodesApi>(http).GetNodesAsync(OrgId, null, null, null, CancellationToken.None);
        }
        catch (ApiException)
        {
            // The stub answers 400; only the request URI matters here.
        }

        capture.RequestUri.Should().NotBeNull();
        capture.RequestUri!.AbsolutePath.Should().Be($"/api/v1/organizations/{OrgId}/nodes");
        GatewayRoutes.Resolve(capture.RequestUri!.AbsolutePath)!.ClusterId.Should().Be("nodes");
    }

    [Fact]
    public async Task NodesList_ThroughRefit_SendsPageAndPageSize()
    {
        using var capture = new CaptureHandler();
        using var http = new HttpClient(capture) { BaseAddress = new Uri(new CliConfig().EffectiveNodesUrl) };

        try
        {
            await RestService.For<INodesApi>(http).GetNodesAsync(OrgId, page: 2, pageSize: 5, status: null, ct: CancellationToken.None);
        }
        catch (ApiException)
        {
            // The stub answers 400; only the request URI matters here.
        }

        capture.RequestUri.Should().NotBeNull();
        capture.RequestUri!.Query.Should().Contain("page=2").And.Contain("pageSize=5").And.NotContain("skip").And.NotContain("take");
    }

    [Fact]
    public void NodesList_ReturnType_IsTheContractsPage()
    {
        typeof(INodesApi).GetMethod(nameof(INodesApi.GetNodesAsync))!.ReturnType
            .Should().Be<Task<Dhadgar.Contracts.FilteredPagedResponse<Dhadgar.Contracts.Nodes.NodeListItemResponse>>>();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{}"),
                RequestMessage = request
            });
        }
    }

    /// <summary>The Refit path literal of one method, read from its attribute.</summary>
    private static string RefitPath(Type api, string methodName)
    {
        var method = api.GetMethods().Single(m => m.Name == methodName);
        return method.GetCustomAttributes(inherit: true)
            .OfType<Refit.HttpMethodAttribute>()
            .Single()
            .Path;
    }

    private static string PathOf(string url) => new Uri(url, UriKind.Absolute).AbsolutePath;

    private sealed record GatewayRoute(string Name, string ClusterId, int Order, string Pattern, string? RemovePrefix);

    private sealed record ResolvedRoute(string Name, string ClusterId, string ForwardedPath);

    private static class GatewayRoutes
    {
        private static readonly string AppSettingsPath = Path.Combine(FindRepoRoot(), "src", "Dhadgar.Gateway", "appsettings.json");

        public static ResolvedRoute? Resolve(string path)
        {
            GatewayRoute? best = null;
            var bestLiterals = -1;

            foreach (var route in Load())
            {
                if (!TryMatch(route.Pattern, path, out var literals))
                {
                    continue;
                }

                if (literals > bestLiterals || (literals == bestLiterals && route.Order < best!.Order))
                {
                    best = route;
                    bestLiterals = literals;
                }
            }

            return best is null
                ? null
                : new ResolvedRoute(best.Name, best.ClusterId, RemovePrefix(path, best.RemovePrefix));
        }

        private static List<GatewayRoute> Load()
        {
            // The Gateway's appsettings.json may carry comments and trailing commas.
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath), options);
            var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
            var result = new List<GatewayRoute>();

            foreach (var property in routes.EnumerateObject())
            {
                var value = property.Value;
                var pattern = value.GetProperty("Match").GetProperty("Path").GetString()!;
                var clusterId = value.GetProperty("ClusterId").GetString()!;
                var order = value.TryGetProperty("Order", out var orderElement) ? orderElement.GetInt32() : int.MaxValue;

                string? removePrefix = null;
                if (value.TryGetProperty("Transforms", out var transforms))
                {
                    foreach (var transform in transforms.EnumerateArray())
                    {
                        if (transform.TryGetProperty("PathRemovePrefix", out var prefix))
                        {
                            removePrefix = prefix.GetString();
                        }
                    }
                }

                result.Add(new GatewayRoute(property.Name, clusterId, order, pattern, removePrefix));
            }

            return result;
        }

        private static bool TryMatch(string pattern, string path, out int literals)
        {
            var patternSegments = Segments(pattern);
            var pathSegments = Segments(path);
            literals = 0;

            for (var i = 0; i < patternSegments.Length; i++)
            {
                var segment = patternSegments[i];

                if (segment.StartsWith("{**", StringComparison.Ordinal) && segment.EndsWith('}'))
                {
                    return true;
                }

                if (i >= pathSegments.Length)
                {
                    return false;
                }

                if (segment.StartsWith('{') && segment.EndsWith('}'))
                {
                    if (pathSegments[i].Length == 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (!string.Equals(segment, pathSegments[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                literals++;
            }

            return patternSegments.Length == pathSegments.Length;
        }

        private static string RemovePrefix(string path, string? prefix)
        {
            if (prefix is null)
            {
                return path;
            }

            var normalized = prefix.TrimEnd('/');
            if (path.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return "/";
            }

            return path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)
                ? path[normalized.Length..]
                : path;
        }

        private static string[] Segments(string path) =>
            path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln"))
                    || Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root not found above the test output directory.");
        }
    }
}
