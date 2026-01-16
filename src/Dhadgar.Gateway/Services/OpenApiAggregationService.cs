using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Yarp.ReverseProxy.Configuration;

namespace Dhadgar.Gateway.Services;

/// <summary>
/// Aggregates OpenAPI specifications from all downstream services into a single unified document.
/// </summary>
public class OpenApiAggregationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenApiAggregationService> _logger;
    private readonly IProxyConfigProvider _proxyConfigProvider;

    private const string CacheKey = "AggregatedOpenApiSpec";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // Map cluster names to display names and route prefixes
    private static readonly Dictionary<string, (string DisplayName, string RoutePrefix)> ServiceMappings = new()
    {
        ["identity"] = ("Identity", "/api/v1/identity"),
        ["servers"] = ("Servers", "/api/v1/servers"),
        ["nodes"] = ("Nodes", "/api/v1/nodes"),
        ["tasks"] = ("Tasks", "/api/v1/tasks"),
        ["files"] = ("Files", "/api/v1/files"),
        ["mods"] = ("Mods", "/api/v1/mods"),
        ["console"] = ("Console", "/api/v1/console"),
        ["billing"] = ("Billing", "/api/v1/billing"),
        ["notifications"] = ("Notifications", "/api/v1/notifications"),
        ["firewall"] = ("Firewall", "/api/v1/firewall"),
        ["secrets"] = ("Secrets", "/api/v1/secrets"),
        ["discord"] = ("Discord", "/api/v1/discord"),
    };

    public OpenApiAggregationService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<OpenApiAggregationService> logger,
        IProxyConfigProvider proxyConfigProvider)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _proxyConfigProvider = proxyConfigProvider;
    }

    public async Task<JsonObject> GetAggregatedSpecAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out JsonObject? cached) && cached is not null)
        {
            return cached;
        }

        var aggregated = await BuildAggregatedSpecAsync(cancellationToken);

        _cache.Set(CacheKey, aggregated, CacheDuration);

        return aggregated;
    }

    private async Task<JsonObject> BuildAggregatedSpecAsync(CancellationToken cancellationToken)
    {
        var aggregated = new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject
            {
                ["title"] = "Meridian Console API",
                ["description"] = "Unified API documentation for all Meridian Console services",
                ["version"] = "v1"
            },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject(),
                ["securitySchemes"] = new JsonObject
                {
                    ["Bearer"] = new JsonObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["bearerFormat"] = "JWT",
                        ["description"] = "Enter your JWT token"
                    }
                }
            },
            ["tags"] = new JsonArray()
        };

        var paths = aggregated["paths"]!.AsObject();
        var schemas = aggregated["components"]!["schemas"]!.AsObject();
        var tags = aggregated["tags"]!.AsArray();

        var client = _httpClientFactory.CreateClient("OpenApiAggregation");

        // Get service addresses from YARP configuration
        var proxyConfig = _proxyConfigProvider.GetConfig();
        var services = new List<(string Name, string BaseUrl, string RoutePrefix)>();

        foreach (var cluster in proxyConfig.Clusters)
        {
            if (!ServiceMappings.TryGetValue(cluster.ClusterId, out var mapping))
            {
                continue;
            }

            // Get the first healthy destination address
            var destination = cluster.Destinations?.Values.FirstOrDefault();
            if (destination?.Address is null)
            {
                _logger.LogWarning("No destination found for cluster {ClusterId}", cluster.ClusterId);
                continue;
            }

            services.Add((mapping.DisplayName, destination.Address.TrimEnd('/'), mapping.RoutePrefix));
        }

        var tasks = services.Select(async service =>
        {
            try
            {
                var swaggerUrl = $"{service.BaseUrl}/swagger/v1/swagger.json";
                _logger.LogDebug("Fetching OpenAPI spec from {Service} at {Url}", service.Name, swaggerUrl);

                var response = await client.GetAsync(swaggerUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch OpenAPI spec from {Service}: {StatusCode}",
                        service.Name, response.StatusCode);
                    return (service, Spec: (JsonObject?)null);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var spec = JsonNode.Parse(json)?.AsObject();

                return (service, Spec: spec);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching OpenAPI spec from {Service}", service.Name);
                return (service, Spec: (JsonObject?)null);
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (service, spec) in results)
        {
            if (spec is null) continue;

            // Add service tag
            tags.Add(new JsonObject
            {
                ["name"] = service.Name,
                ["description"] = $"{service.Name} service endpoints"
            });

            // Merge paths with route prefix
            if (spec["paths"] is JsonObject servicePaths)
            {
                foreach (var (path, pathItem) in servicePaths)
                {
                    if (pathItem is null) continue;

                    var prefixedPath = $"{service.RoutePrefix}{path}";
                    var clonedPathItem = JsonNode.Parse(pathItem.ToJsonString())!.AsObject();

                    // Add service tag to all operations and update schema refs
                    foreach (var (method, operation) in clonedPathItem)
                    {
                        if (operation is JsonObject op)
                        {
                            // Replace tags with just the service name for clean grouping
                            op["tags"] = new JsonArray(service.Name);

                            // Update schema references to include service prefix
                            UpdateSchemaRefs(op, service.Name);
                        }
                    }

                    paths[prefixedPath] = clonedPathItem;
                }
            }

            // Merge schemas with service prefix to avoid conflicts
            if (spec["components"]?["schemas"] is JsonObject serviceSchemas)
            {
                foreach (var (schemaName, schema) in serviceSchemas)
                {
                    if (schema is null) continue;

                    var prefixedName = $"{service.Name}_{schemaName}";
                    var clonedSchema = JsonNode.Parse(schema.ToJsonString());

                    // Update internal schema refs
                    if (clonedSchema is JsonObject schemaObj)
                    {
                        UpdateSchemaRefs(schemaObj, service.Name);
                    }

                    schemas[prefixedName] = clonedSchema;
                }
            }
        }

        return aggregated;
    }

    private static void UpdateSchemaRefs(JsonNode node, string servicePrefix)
    {
        if (node is JsonObject obj)
        {
            // Check for $ref and update it
            if (obj["$ref"] is JsonValue refValue)
            {
                var refString = refValue.GetValue<string>();
                if (refString.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                {
                    var schemaName = refString["#/components/schemas/".Length..];
                    obj["$ref"] = $"#/components/schemas/{servicePrefix}_{schemaName}";
                }
            }

            // Recursively process all properties
            foreach (var (_, value) in obj)
            {
                if (value is not null)
                {
                    UpdateSchemaRefs(value, servicePrefix);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    UpdateSchemaRefs(item, servicePrefix);
                }
            }
        }
    }
}
