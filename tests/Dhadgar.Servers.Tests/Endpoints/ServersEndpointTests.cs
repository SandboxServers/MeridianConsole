using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Servers.Tests.Endpoints;

[Collection("Servers Integration")]
public sealed class ServersEndpointTests
{
    private readonly ServersWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ServersEndpointTests(ServersWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CreateServerRequest ValidCreateRequest(string? name = null) => new(
        Name: name ?? $"test-{Guid.NewGuid():N}"[..20],
        DisplayName: "Test Server",
        GameType: "minecraft",
        CpuLimitMillicores: 2000,
        MemoryLimitMb: 4096,
        DiskLimitMb: 10240,
        TemplateId: null,
        StartupCommand: null,
        GameSettings: null,
        AutoStart: false,
        AutoRestartOnCrash: false,
        Ports: [new CreateServerPortRequest("game", "tcp", 25565, null, true)],
        Tags: ["survival", "vanilla"]);

    private static string BaseUrl(Guid orgId) => $"/organizations/{orgId}/servers";

    // --- Create ---

    [Fact]
    public async Task CreateServer_ValidRequest_Returns201()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var request = ValidCreateRequest();
        var response = await client.PostAsJsonAsync(BaseUrl(orgId), request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await response.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.Name.Should().Be(request.Name);
        detail.GameType.Should().Be("minecraft");
        detail.Status.Should().Be("Created");
        detail.PowerState.Should().Be("Off");
        detail.OrganizationId.Should().Be(orgId);
        detail.Ports.Should().HaveCount(1);
        detail.Tags.Should().Contain("survival");
    }

    [Fact]
    public async Task CreateServer_DuplicateName_Returns409()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var name = $"dup-{Guid.NewGuid():N}"[..16];
        var request = ValidCreateRequest(name);

        var first = await client.PostAsJsonAsync(BaseUrl(orgId), request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(BaseUrl(orgId), request);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateServer_InvalidRequest_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var request = ValidCreateRequest() with { Name = "", GameType = "" };
        var response = await client.PostAsJsonAsync(BaseUrl(orgId), request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateServer_NoAuth_Returns401()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var request = ValidCreateRequest();
        var response = await client.PostAsJsonAsync(BaseUrl(orgId), request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- Get ---

    [Fact]
    public async Task GetServer_Exists_Returns200()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);

        var response = await client.GetAsync($"{BaseUrl(orgId)}/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(created.Id);
        detail.Name.Should().Be(created.Name);
    }

    [Fact]
    public async Task GetServer_NotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync($"{BaseUrl(orgId)}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetServer_WrongOrg_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);

        // Try to access from a different org
        using var otherClient = _factory.CreateAuthenticatedClient(userId, otherOrgId);
        var response = await otherClient.GetAsync($"{BaseUrl(otherOrgId)}/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- List ---

    [Fact]
    public async Task ListServers_ReturnsPagedResponse()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // Create two servers
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());

        var response = await client.GetAsync(BaseUrl(orgId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListServers_FilterByGameType_ReturnsFiltered()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest() with { GameType = "minecraft" });
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest() with { GameType = "valheim" });

        var response = await client.GetAsync($"{BaseUrl(orgId)}?gameType=minecraft");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");

        foreach (var item in items.EnumerateArray())
        {
            item.GetProperty("gameType").GetString().Should().Be("minecraft");
        }
    }

    [Fact]
    public async Task ListServers_Pagination_RespectsPageSize()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // Create 3 servers
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());

        var response = await client.GetAsync($"{BaseUrl(orgId)}?pageSize=2&page=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        doc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ListServers_SortByName_ReturnsSorted()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest("zzz-server"));
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest("aaa-server"));

        var response = await client.GetAsync($"{BaseUrl(orgId)}?sortBy=name&sortOrder=asc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        var names = items.EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();

        names.Should().BeInAscendingOrder();
    }

    // --- Update ---

    [Fact]
    public async Task UpdateServer_ValidRequest_Returns200()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);

        var updateRequest = new UpdateServerRequest("updated1", "Updated Display", ["new-tag"]);
        var response = await client.PatchAsJsonAsync($"{BaseUrl(orgId)}/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);
        detail!.Name.Should().Be("updated1");
        detail.DisplayName.Should().Be("Updated Display");
        detail.Tags.Should().Contain("new-tag");
    }

    [Fact]
    public async Task UpdateServer_DuplicateName_Returns409()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var name1 = $"a{Guid.NewGuid():N}"[..12];
        var name2 = $"b{Guid.NewGuid():N}"[..12];
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest(name1));
        var createResponse2 = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest(name2));
        var created2 = await createResponse2.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);

        var updateRequest = new UpdateServerRequest(name1, null, null);
        var response = await client.PatchAsJsonAsync($"{BaseUrl(orgId)}/{created2!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateServer_NotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var updateRequest = new UpdateServerRequest("newname", null, null);
        var response = await client.PatchAsJsonAsync($"{BaseUrl(orgId)}/{Guid.NewGuid()}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteServer_Exists_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerDetail>(JsonOptions);

        var response = await client.DeleteAsync($"{BaseUrl(orgId)}/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it no longer appears
        var getResponse = await client.GetAsync($"{BaseUrl(orgId)}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteServer_NotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync($"{BaseUrl(orgId)}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
