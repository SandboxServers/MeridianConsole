using System.Net;
using System.Text.Json.Nodes;
using Dhadgar.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class OpenApiAggregationServiceTests
{
    [Fact]
    public async Task GetAggregatedSpecAsync_ReturnsValidOpenApiDocument()
    {
        // Arrange
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = CreateMinimalSwaggerJson("Identity API")
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        Assert.Equal("3.0.1", result["openapi"]?.GetValue<string>());
        Assert.Equal("Meridian Console API", result["info"]?["title"]?.GetValue<string>());
        Assert.Equal("v1", result["info"]?["version"]?.GetValue<string>());
        Assert.NotNull(result["paths"]);
        Assert.NotNull(result["components"]);
        Assert.NotNull(result["tags"]);
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_PrefixesPathsWithServiceRoutePrefix()
    {
        // Arrange
        var swaggerJson = CreateSwaggerJsonWithPath("/users", "get", "ListUsers");
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var paths = result["paths"]!.AsObject();
        Assert.True(paths.ContainsKey("/api/v1/identity/users"));
        Assert.False(paths.ContainsKey("/users"));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_AddsServiceTagToOperations()
    {
        // Arrange
        var swaggerJson = CreateSwaggerJsonWithPath("/users", "get", "ListUsers");
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var operation = result["paths"]?["/api/v1/identity/users"]?["get"];
        Assert.NotNull(operation);
        var tags = operation!["tags"]?.AsArray();
        Assert.NotNull(tags);
        Assert.Contains("Identity", tags!.Select(t => t?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_PrefixesSchemaNames()
    {
        // Arrange
        var swaggerJson = CreateSwaggerJsonWithSchema("UserDto", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject { ["type"] = "string" },
                ["email"] = new JsonObject { ["type"] = "string" }
            }
        });
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var schemas = result["components"]?["schemas"]?.AsObject();
        Assert.NotNull(schemas);
        Assert.True(schemas!.ContainsKey("Identity_UserDto"));
        Assert.False(schemas.ContainsKey("UserDto"));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_UpdatesSchemaReferences()
    {
        // Arrange
        var swaggerJson = CreateSwaggerJsonWithSchemaRef("/users", "UserDto");
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var responseSchema = result["paths"]?["/api/v1/identity/users"]?["get"]?["responses"]?["200"]?["content"]?["application/json"]?["schema"];
        Assert.NotNull(responseSchema);
        Assert.Equal("#/components/schemas/Identity_UserDto", responseSchema!["$ref"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_MergesMultipleServices()
    {
        // Arrange
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010"), ("servers", "http://localhost:5030")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = CreateSwaggerJsonWithPath("/users", "get", "ListUsers"),
                ["http://localhost:5030/openapi/v1.json"] = CreateSwaggerJsonWithPath("/servers", "get", "ListServers")
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var paths = result["paths"]!.AsObject();
        Assert.True(paths.ContainsKey("/api/v1/identity/users"));
        Assert.True(paths.ContainsKey("/api/v1/servers/servers"));

        var tags = result["tags"]!.AsArray();
        Assert.Contains("Identity", tags.Select(t => t?["name"]?.GetValue<string>()));
        Assert.Contains("Servers", tags.Select(t => t?["name"]?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_GracefullyHandlesUnavailableServices()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("5010", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CreateSwaggerJsonWithPath("/users", "get", "ListUsers"))
                };
            }
            // Servers service unavailable
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        using var fixture = CreateServiceWithHandler(
            clusters: [("identity", "http://localhost:5010"), ("servers", "http://localhost:5030")],
            handler: handler);

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Should still have Identity paths even though Servers failed
        var paths = result["paths"]!.AsObject();
        Assert.True(paths.ContainsKey("/api/v1/identity/users"));
        Assert.False(paths.ContainsKey("/api/v1/servers/servers"));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_GracefullyHandlesTimeout()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(_ =>
            throw new TaskCanceledException("Request timed out"));

        using var fixture = CreateServiceWithHandler(
            clusters: [("identity", "http://localhost:5010")],
            handler: handler);

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Should return valid empty spec
        Assert.Equal("3.0.1", result["openapi"]?.GetValue<string>());
        var paths = result["paths"]!.AsObject();
        Assert.Empty(paths);
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_CachesResult()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateMinimalSwaggerJson("Identity API"))
            };
        });

        using var fixture = CreateServiceWithHandler(
            clusters: [("identity", "http://localhost:5010")],
            handler: handler);

        // Act
        await fixture.Service.GetAggregatedSpecAsync();
        await fixture.Service.GetAggregatedSpecAsync();
        await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Should only call HTTP once due to caching
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_IncludesSecurityScheme()
    {
        // Arrange
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = CreateMinimalSwaggerJson("Identity API")
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert
        var securitySchemes = result["components"]?["securitySchemes"]?.AsObject();
        Assert.NotNull(securitySchemes);
        Assert.True(securitySchemes!.ContainsKey("Bearer"));
        Assert.Equal("http", securitySchemes["Bearer"]?["type"]?.GetValue<string>());
        Assert.Equal("bearer", securitySchemes["Bearer"]?["scheme"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_SkipsUnknownClusters()
    {
        // Arrange - betterauth cluster is not in ServiceMappings
        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010"), ("betterauth", "http://localhost:5130")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = CreateSwaggerJsonWithPath("/users", "get", "ListUsers"),
                ["http://localhost:5130/openapi/v1.json"] = CreateSwaggerJsonWithPath("/auth", "post", "Login")
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Should only have identity, not betterauth
        var paths = result["paths"]!.AsObject();
        Assert.True(paths.ContainsKey("/api/v1/identity/users"));
        Assert.False(paths.ContainsKey("/api/v1/betterauth/auth"));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_PreservesExistingTags()
    {
        // Arrange
        var swaggerJson = new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = "Test", ["version"] = "1.0" },
            ["paths"] = new JsonObject
            {
                ["/users"] = new JsonObject
                {
                    ["get"] = new JsonObject
                    {
                        ["operationId"] = "ListUsers",
                        ["tags"] = new JsonArray { "Users", "Admin" },
                        ["responses"] = new JsonObject { ["200"] = new JsonObject { ["description"] = "Success" } }
                    }
                }
            }
        }.ToJsonString();

        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Should have Identity tag plus existing tags
        var tags = result["paths"]?["/api/v1/identity/users"]?["get"]?["tags"]?.AsArray();
        Assert.NotNull(tags);
        Assert.Contains("Identity", tags!.Select(t => t?.GetValue<string>()));
        Assert.Contains("Users", tags.Select(t => t?.GetValue<string>()));
        Assert.Contains("Admin", tags.Select(t => t?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAggregatedSpecAsync_HandlesNestedSchemaRefs()
    {
        // Arrange
        var swaggerJson = new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = "Test", ["version"] = "1.0" },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    ["Organization"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["owner"] = new JsonObject
                            {
                                ["$ref"] = "#/components/schemas/User"
                            },
                            ["members"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject
                                {
                                    ["$ref"] = "#/components/schemas/User"
                                }
                            }
                        }
                    },
                    ["User"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = new JsonObject { ["type"] = "string" }
                        }
                    }
                }
            }
        }.ToJsonString();

        using var fixture = CreateService(
            clusters: [("identity", "http://localhost:5010")],
            responses: new Dictionary<string, string>
            {
                ["http://localhost:5010/openapi/v1.json"] = swaggerJson
            });

        // Act
        var result = await fixture.Service.GetAggregatedSpecAsync();

        // Assert - Nested refs should be updated
        var orgSchema = result["components"]?["schemas"]?["Identity_Organization"];
        Assert.NotNull(orgSchema);

        var ownerRef = orgSchema!["properties"]?["owner"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/components/schemas/Identity_User", ownerRef);

        var membersItemRef = orgSchema["properties"]?["members"]?["items"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/components/schemas/Identity_User", membersItemRef);
    }

    #region Factory Methods

    /// <summary>
    /// Disposable wrapper that holds the service and its dependencies for proper cleanup.
    /// </summary>
    private sealed class ServiceFixture : IDisposable
    {
        private readonly MemoryCache _cache;

        public OpenApiAggregationService Service { get; }

        public ServiceFixture(
            IHttpClientFactory httpClientFactory,
            IProxyConfigProvider proxyConfigProvider)
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
            Service = new OpenApiAggregationService(
                httpClientFactory,
                _cache,
                NullLogger<OpenApiAggregationService>.Instance,
                proxyConfigProvider);
        }

        public void Dispose()
        {
            _cache.Dispose();
        }
    }

    private static ServiceFixture CreateService(
        (string ClusterId, string Address)[] clusters,
        Dictionary<string, string> responses)
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (responses.TryGetValue(url, out var content))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return CreateServiceWithHandler(clusters, handler);
    }

    private static ServiceFixture CreateServiceWithHandler(
        (string ClusterId, string Address)[] clusters,
        TestHttpMessageHandler handler)
    {
        var httpClientFactory = new TestHttpClientFactory(handler);
        var proxyConfigProvider = new TestProxyConfigProvider(clusters);

        return new ServiceFixture(httpClientFactory, proxyConfigProvider);
    }

    #endregion

    #region Swagger JSON Builders

    private static string CreateMinimalSwaggerJson(string title)
    {
        return new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = title, ["version"] = "1.0" },
            ["paths"] = new JsonObject()
        }.ToJsonString();
    }

    private static string CreateSwaggerJsonWithPath(string path, string method, string operationId)
    {
        return new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = "Test API", ["version"] = "1.0" },
            ["paths"] = new JsonObject
            {
                [path] = new JsonObject
                {
                    [method] = new JsonObject
                    {
                        ["operationId"] = operationId,
                        ["responses"] = new JsonObject
                        {
                            ["200"] = new JsonObject { ["description"] = "Success" }
                        }
                    }
                }
            }
        }.ToJsonString();
    }

    private static string CreateSwaggerJsonWithSchema(string schemaName, JsonObject schemaDefinition)
    {
        return new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = "Test API", ["version"] = "1.0" },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    [schemaName] = schemaDefinition
                }
            }
        }.ToJsonString();
    }

    private static string CreateSwaggerJsonWithSchemaRef(string path, string schemaName)
    {
        return new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject { ["title"] = "Test API", ["version"] = "1.0" },
            ["paths"] = new JsonObject
            {
                [path] = new JsonObject
                {
                    ["get"] = new JsonObject
                    {
                        ["operationId"] = "GetItems",
                        ["responses"] = new JsonObject
                        {
                            ["200"] = new JsonObject
                            {
                                ["description"] = "Success",
                                ["content"] = new JsonObject
                                {
                                    ["application/json"] = new JsonObject
                                    {
                                        ["schema"] = new JsonObject
                                        {
                                            ["$ref"] = $"#/components/schemas/{schemaName}"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    [schemaName] = new JsonObject
                    {
                        ["type"] = "object"
                    }
                }
            }
        }.ToJsonString();
    }

    #endregion

    #region Test Doubles

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(5) };
        }
    }

    private sealed class TestProxyConfigProvider : IProxyConfigProvider
    {
        private readonly TestProxyConfig _config;

        public TestProxyConfigProvider((string ClusterId, string Address)[] clusters)
        {
            _config = new TestProxyConfig(clusters);
        }

        public IProxyConfig GetConfig() => _config;
    }

    private sealed class TestProxyConfig : IProxyConfig
    {
        public TestProxyConfig((string ClusterId, string Address)[] clusters)
        {
            Clusters = clusters.Select(c => new ClusterConfig
            {
                ClusterId = c.ClusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new DestinationConfig { Address = c.Address }
                }
            }).ToList();
        }

        public IReadOnlyList<RouteConfig> Routes { get; } = [];
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; } = new TestChangeToken();
    }

    private sealed class TestChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    #endregion
}
