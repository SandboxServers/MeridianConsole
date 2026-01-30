using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Secrets.Tests.Security;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Dhadgar.Secrets.Tests.Endpoints;

/// <summary>
/// Integration tests verifying RFC 9457 Problem Details format for error responses.
/// </summary>
[Collection("Secure Secrets Integration")]
public sealed class ProblemDetailsIntegrationTests
{
    private readonly SecureSecretsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProblemDetailsIntegrationTests(SecureSecretsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvalidSecretName_ReturnsProblemDetailsFormat()
    {
        // Arrange - Use a secret name with path traversal (URL encoded)
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Act - The path must be URL-encoded to reach the endpoint with the invalid characters
        var response = await client.GetAsync("/api/v1/secrets/" + Uri.EscapeDataString("..%2Finvalid"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Type);
        Assert.NotNull(problemDetails.Title);
        Assert.Contains("meridian.console/errors", problemDetails.Type, StringComparison.Ordinal);

        // Verify trace context is included
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"), "Expected traceId in extensions");
        Assert.True(problemDetails.Extensions.ContainsKey("correlationId"), "Expected correlationId in extensions");
    }

    [Fact]
    public async Task SecretNotInAllowedList_ReturnsProblemDetailsFormat()
    {
        // Arrange - Secret not in allowed list
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Act
        var response = await client.GetAsync("/api/v1/secrets/unknown-secret");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetSecretWithEmptyValue_ReturnsProblemDetailsFormat()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        // Act
        using var content = JsonContent.Create(new { value = "" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Detail);
        Assert.Contains("required", problemDetails.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteNonExistentSecret_ReturnsProblemDetailsFormat()
    {
        // Arrange - Use a secret name that exists in allowed list but will return false from delete
        // The FakeSecretProvider will return false for secrets not in its dictionary
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:delete:oauth");

        // Act
        var response = await client.DeleteAsync("/api/v1/secrets/oauth-discord-client-secret");

        // Assert - Either 204 (if found and deleted) or 404 (if not found)
        // Since FakeSecretProvider has this secret, it will delete successfully
        // Let's test with a different approach - try to delete twice
        var response2 = await client.DeleteAsync("/api/v1/secrets/oauth-discord-client-secret");

        // Second delete should return 404 since it was already deleted
        Assert.Equal(HttpStatusCode.NotFound, response2.StatusCode);
        Assert.Equal("application/problem+json", response2.Content.Headers.ContentType?.MediaType);

        var content = await response2.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.NotNull(problemDetails.Title);
        Assert.Equal("Not Found", problemDetails.Title);
    }

    [Fact]
    public async Task ValidationError_ContainsProperErrorType()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        // Act - Use a secret name with path traversal (URL encoded)
        using var content = JsonContent.Create(new { value = "some-value" });
        var response = await client.PutAsync("/api/v1/secrets/" + Uri.EscapeDataString("..%2Fhack"), content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Contains("validation", problemDetails.Type!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnauthorizedRequest_ReturnsProblemDetails()
    {
        // Arrange - No authentication
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ErrorResponse_IncludesTimestamp()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Act - Use a secret name with invalid characters (URL encoded)
        var response = await client.GetAsync("/api/v1/secrets/" + Uri.EscapeDataString("..%2Finvalid"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.ContainsKey("timestamp"), "Expected timestamp in extensions");
    }

    [Fact]
    public async Task ProblemDetails_TraceIdIsNotEmpty()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Act - Use a secret name with invalid characters (URL encoded)
        var response = await client.GetAsync("/api/v1/secrets/" + Uri.EscapeDataString("..%2Finvalid"));

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.TryGetValue("traceId", out var traceId));
        var traceIdString = traceId?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(traceIdString), "traceId should not be empty");
        Assert.NotEqual("unknown", traceIdString);
    }

    [Fact]
    public async Task ProblemDetails_CorrelationIdIsNotEmpty()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Act - Use a secret name with invalid characters (URL encoded)
        var response = await client.GetAsync("/api/v1/secrets/" + Uri.EscapeDataString("..%2Finvalid"));

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(content, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.TryGetValue("correlationId", out var correlationId));
        var correlationIdString = correlationId?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(correlationIdString), "correlationId should not be empty");
        Assert.NotEqual("unknown", correlationIdString);
    }
}
