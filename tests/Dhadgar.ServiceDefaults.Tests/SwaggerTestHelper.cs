using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests;

/// <summary>
/// Shared test helper for verifying Swagger/OpenAPI endpoints work correctly.
/// Use this in any service test project to validate Swagger configuration.
/// </summary>
public static class SwaggerTestHelper
{
    /// <summary>
    /// Verifies that the /swagger/v1/swagger.json endpoint returns valid OpenAPI JSON.
    /// </summary>
    /// <typeparam name="TEntryPoint">The Program class of the service under test.</typeparam>
    /// <param name="factory">The WebApplicationFactory for the service.</param>
    /// <param name="expectedTitle">Optional: expected API title in the OpenAPI info section.</param>
    /// <param name="swaggerPath">Path to swagger.json. Defaults to /swagger/v1/swagger.json.</param>
    public static async Task VerifySwaggerEndpointAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory,
        string? expectedTitle = null,
        string swaggerPath = "/swagger/v1/swagger.json")
        where TEntryPoint : class
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(swaggerPath);

        // Assert - Endpoint returns OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert - Content type is JSON
        Assert.Contains("application/json", response.Content.Headers.ContentType?.MediaType ?? "");

        // Assert - Response is valid JSON
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(content), "Swagger response should not be empty");

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Assert - Has required OpenAPI fields
        Assert.True(root.TryGetProperty("openapi", out var openapi), "Missing 'openapi' field");
        Assert.StartsWith("3.", openapi.GetString(), StringComparison.Ordinal);

        Assert.True(root.TryGetProperty("info", out var info), "Missing 'info' field");
        Assert.True(info.TryGetProperty("title", out var title), "Missing 'info.title' field");
        Assert.True(info.TryGetProperty("version", out _), "Missing 'info.version' field");

        if (expectedTitle is not null)
        {
            Assert.Equal(expectedTitle, title.GetString());
        }

        Assert.True(root.TryGetProperty("paths", out _), "Missing 'paths' field");
    }

    /// <summary>
    /// Verifies that the Swagger UI endpoint returns HTML.
    /// </summary>
    /// <typeparam name="TEntryPoint">The Program class of the service under test.</typeparam>
    /// <param name="factory">The WebApplicationFactory for the service.</param>
    /// <param name="swaggerUiPath">Path to Swagger UI. Defaults to /swagger/index.html.</param>
    public static async Task VerifySwaggerUiAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory,
        string swaggerUiPath = "/swagger/index.html")
        where TEntryPoint : class
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(swaggerUiPath);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType ?? "");

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the swagger.json contains expected paths.
    /// </summary>
    /// <typeparam name="TEntryPoint">The Program class of the service under test.</typeparam>
    /// <param name="factory">The WebApplicationFactory for the service.</param>
    /// <param name="expectedPaths">Paths that should exist in the OpenAPI spec.</param>
    /// <param name="swaggerPath">Path to swagger.json. Defaults to /swagger/v1/swagger.json.</param>
    public static async Task VerifySwaggerContainsPathsAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory,
        string[] expectedPaths,
        string swaggerPath = "/swagger/v1/swagger.json")
        where TEntryPoint : class
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(swaggerPath);
        var content = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(content);
        var paths = doc.RootElement.GetProperty("paths");

        // Assert - All expected paths exist
        foreach (var path in expectedPaths)
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Expected path '{path}' not found in swagger.json");
        }
    }

    /// <summary>
    /// Verifies basic service health endpoints are documented in swagger.
    /// Most services should have /, /hello, and /healthz endpoints.
    /// </summary>
    /// <typeparam name="TEntryPoint">The Program class of the service under test.</typeparam>
    /// <param name="factory">The WebApplicationFactory for the service.</param>
    /// <param name="swaggerPath">Path to swagger.json. Defaults to /swagger/v1/swagger.json.</param>
    public static async Task VerifyHealthEndpointsDocumentedAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory,
        string swaggerPath = "/swagger/v1/swagger.json")
        where TEntryPoint : class
    {
        await VerifySwaggerContainsPathsAsync(factory, ["/", "/hello", "/healthz"], swaggerPath);
    }
}
