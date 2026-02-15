using System.Net;
using System.Net.Http.Json;
using Dhadgar.Console.Data;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Contracts.Console;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EntityOutputType = Dhadgar.Console.Data.Entities.ConsoleOutputType;

namespace Dhadgar.Console.Tests.Endpoints;

[Collection("Console Integration")]
public class ConsoleEndpointTests
{
    private readonly ConsoleWebApplicationFactory _factory;

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public ConsoleEndpointTests(ConsoleWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedHistoryEntriesAsync(int count = 5)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();

        for (var i = 0; i < count; i++)
        {
            db.ConsoleHistory.Add(new ConsoleHistoryEntry
            {
                ServerId = ServerId,
                OrganizationId = OrgId,
                OutputType = EntityOutputType.StdOut,
                Content = $"Test output line {i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                SequenceNumber = i
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetHistory_Authenticated_Returns200()
    {
        await SeedHistoryEntriesAsync();
        using var client = _factory.CreateAuthenticatedClient(UserId, OrgId);

        var response = await client.GetAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/history?lineCount=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ConsoleHistoryDto>();
        dto.Should().NotBeNull();
        dto!.ServerId.Should().Be(ServerId);
    }

    [Fact]
    public async Task GetHistory_WrongOrg_Returns403()
    {
        var wrongOrgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(UserId, wrongOrgId);

        var response = await client.GetAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/history");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetHistory_NoAuth_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHistory_ClampsLineCount_ReturnsSuccess()
    {
        await SeedHistoryEntriesAsync();
        using var client = _factory.CreateAuthenticatedClient(UserId, OrgId);

        var response = await client.GetAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/history?lineCount=5000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ConsoleHistoryDto>();
        dto.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchHistory_ValidRequest_Returns200()
    {
        await SeedHistoryEntriesAsync();
        using var client = _factory.CreateAuthenticatedClient(UserId, OrgId);

        var request = new SearchConsoleHistoryRequest(ServerId, Query: "Test");
        var response = await client.PostAsJsonAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ConsoleHistorySearchResult>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchHistory_InvalidRequest_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient(UserId, OrgId);

        var request = new SearchConsoleHistoryRequest(
            ServerId,
            StartTime: new DateTime(2026, 2, 14, 12, 0, 0, DateTimeKind.Utc),
            EndTime: new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc));
        var response = await client.PostAsJsonAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchHistory_WrongOrg_Returns403()
    {
        var wrongOrgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(UserId, wrongOrgId);

        var request = new SearchConsoleHistoryRequest(ServerId, Query: "Test");
        var response = await client.PostAsJsonAsync(
            $"/organizations/{OrgId}/servers/{ServerId}/console/search", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
