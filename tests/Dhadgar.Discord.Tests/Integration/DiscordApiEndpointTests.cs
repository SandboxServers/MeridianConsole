#nullable enable

using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Dhadgar.Discord.Tests.Integration;

/// <summary>
/// Integration tests for Discord API endpoints using WebApplicationFactory.
/// Note: Database-dependent tests (logs endpoint) are excluded due to EF Core provider conflicts
/// in WebApplicationFactory. See SendDiscordNotificationConsumerTests for database coverage.
/// </summary>
public sealed class DiscordApiEndpointTests : IClassFixture<DiscordWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DiscordApiEndpointTests(DiscordWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ReturnsOkWithBotStatus()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();
        Assert.NotNull(content);
        Assert.Equal("Dhadgar.Discord", content.Service);
        Assert.Equal("ok", content.Status);
        // Mock bot service returns Disconnected since we can't mock DiscordSocketClient
        Assert.Equal("Disconnected", content.BotStatus);
    }

    [Fact]
    public async Task ServiceInfo_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetDiscordChannels_WithoutAuth_ReturnsUnauthorized()
    {
        // Act - No admin API key header
        _client.DefaultRequestHeaders.Clear();
        var response = await _client.GetAsync("/api/v1/discord/channels");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDiscordChannels_WithAuth_ReturnsNotConnected()
    {
        // Arrange
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "test-admin-key");

        // Act
        var response = await _client.GetAsync("/api/v1/discord/channels");

        // Assert - Mock bot service returns Disconnected
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<DiscordChannelsResponse>();
        Assert.NotNull(content);
        Assert.False(content.Connected);
    }

    [Fact]
    public async Task GetPlatformHealth_WithoutAuth_ReturnsUnauthorized()
    {
        // Act - No admin API key header
        _client.DefaultRequestHeaders.Clear();
        var response = await _client.GetAsync("/api/v1/platform/health");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPlatformHealth_WithAuth_ReturnsServiceStatuses()
    {
        // Arrange
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "test-admin-key");

        // Act
        var response = await _client.GetAsync("/api/v1/platform/health");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<PlatformHealthResponse>();
        Assert.NotNull(content);
        Assert.Equal(2, content.HealthyCount);
        Assert.Equal(0, content.UnhealthyCount);
    }

    private record HealthCheckResponse(string Service, string Status, string BotStatus);
    private record DiscordChannelsResponse(bool Connected, int GuildCount);
    private record PlatformHealthResponse(int HealthyCount, int UnhealthyCount);
}
