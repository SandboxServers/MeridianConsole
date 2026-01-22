using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Dhadgar.Identity.Tests.Endpoints;

/// <summary>
/// Integration tests verifying RFC 9457 Problem Details format for error responses.
/// Tests focus on endpoints that explicitly return Problem Details format.
/// </summary>
public sealed class ProblemDetailsIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProblemDetailsIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_ReturnsProblemDetails()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act - Send invalid exchange token
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Title);
        Assert.NotNull(problemDetails.Type);
    }

    [Fact]
    public async Task TokenExchange_MissingToken_ReturnsProblemDetails()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act - Send request without exchange token
        using var content = JsonContent.Create(new { exchange_token = (string?)null });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Title);
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_IncludesTraceId()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.TryGetValue("traceId", out var traceId));
        var traceIdString = traceId?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(traceIdString), "traceId should not be empty");
        Assert.NotEqual("unknown", traceIdString);
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_IncludesCorrelationId()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.TryGetValue("correlationId", out var correlationId));
        var correlationIdString = correlationId?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(correlationIdString), "correlationId should not be empty");
        Assert.NotEqual("unknown", correlationIdString);
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_IncludesTimestamp()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.Extensions.ContainsKey("timestamp"), "Expected timestamp in extensions");
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_HasMeridianTypeUri()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.NotNull(problemDetails.Type);
        Assert.Contains("meridian.console/errors", problemDetails.Type, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenExchange_InvalidToken_HasProperTitle()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var content = JsonContent.Create(new { exchange_token = "invalid-token" });
        var response = await client.PostAsync("/exchange", content);

        // Assert
        var responseContent = await response.Content.ReadAsStringAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseContent, JsonOptions);

        Assert.NotNull(problemDetails);
        Assert.Equal("Bad Request", problemDetails.Title);
    }
}
