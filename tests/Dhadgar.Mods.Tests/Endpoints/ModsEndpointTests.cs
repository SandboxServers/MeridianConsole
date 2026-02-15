using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts;
using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Data;
using Dhadgar.Mods.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Mods.Tests.Endpoints;

[Collection("Mods Integration")]
public class ModsEndpointTests
{
    private readonly ModsWebApplicationFactory _factory;

    public ModsEndpointTests(ModsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestOrgId = Guid.NewGuid();

    private static CreateModRequest MakeCreateRequest(
        string? name = null,
        string? slug = null,
        string? gameType = null,
        bool isPublic = false) => new(
            Name: name ?? $"Test Mod {Guid.NewGuid():N}",
            Slug: slug ?? $"test-mod-{Guid.NewGuid():N}"[..30],
            Description: "A test mod",
            Author: "TestAuthor",
            CategoryId: null,
            GameType: gameType ?? "minecraft",
            IsPublic: isPublic,
            ProjectUrl: "https://github.com/test/mod",
            IconUrl: null,
            Tags: ["test", "integration"]);

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMod_ValidRequest_Returns201WithModDetail()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var request = MakeCreateRequest();

        var response = await client.PostAsJsonAsync($"/organizations/{TestOrgId}/mods", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var mod = await response.Content.ReadFromJsonAsync<ModDetail>();
        mod.Should().NotBeNull();
        mod!.Name.Should().Be(request.Name);
        mod.Slug.Should().Be(request.Slug);
        mod.GameType.Should().Be(request.GameType);
        mod.IsPublic.Should().Be(request.IsPublic);
    }

    [Fact]
    public async Task CreateMod_DuplicateSlug_Returns409()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var slug = $"dup-slug-{Guid.NewGuid():N}"[..25];
        var request1 = MakeCreateRequest(slug: slug);
        var request2 = MakeCreateRequest(slug: slug);

        var response1 = await client.PostAsJsonAsync($"/organizations/{TestOrgId}/mods", request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        var response2 = await client.PostAsJsonAsync($"/organizations/{TestOrgId}/mods", request2);
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateMod_InvalidRequest_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var request = new CreateModRequest(
            Name: "",
            Slug: "",
            Description: null,
            Author: null,
            CategoryId: null,
            GameType: "",
            IsPublic: false,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var response = await client.PostAsJsonAsync($"/organizations/{TestOrgId}/mods", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Get ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMod_Exists_Returns200WithDetail()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        var response = await client.GetAsync($"/organizations/{TestOrgId}/mods/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var mod = await response.Content.ReadFromJsonAsync<ModDetail>();
        mod.Should().NotBeNull();
        mod!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetMod_NotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);

        var response = await client.GetAsync($"/organizations/{TestOrgId}/mods/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMod_PrivateFromOtherOrg_Returns404()
    {
        var ownerOrgId = Guid.NewGuid();
        using var ownerClient = _factory.CreateAuthenticatedClient(TestUserId, ownerOrgId);
        var createResponse = await ownerClient.PostAsJsonAsync(
            $"/organizations/{ownerOrgId}/mods", MakeCreateRequest(isPublic: false));
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        var otherOrgId = Guid.NewGuid();
        using var otherClient = _factory.CreateAuthenticatedClient(Guid.NewGuid(), otherOrgId);

        var response = await otherClient.GetAsync($"/organizations/{otherOrgId}/mods/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Get Public ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicMod_PublicMod_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest(isPublic: true));
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        using var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/mods/public/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var mod = await response.Content.ReadFromJsonAsync<ModDetail>();
        mod.Should().NotBeNull();
        mod!.Id.Should().Be(created.Id);
        mod.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicMod_PrivateMod_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest(isPublic: false));
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        using var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/mods/public/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── List ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMods_ReturnsPagedResponse()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a couple of mods
        await client.PostAsJsonAsync($"/organizations/{orgId}/mods", MakeCreateRequest());
        await client.PostAsJsonAsync($"/organizations/{orgId}/mods", MakeCreateRequest());

        var response = await client.GetAsync($"/organizations/{orgId}/mods?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<ModListItem>>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task ListPublicMods_NoAuth_Returns200()
    {
        using var authedClient = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        await authedClient.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest(isPublic: true, gameType: "valheim"));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/mods/public");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<ModListItem>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListPublicMods_FilteredByGameType_ReturnsOnlyMatchingMods()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);
        var uniqueGameType = $"gametype-{Guid.NewGuid():N}"[..30];
        await client.PostAsJsonAsync(
            $"/organizations/{orgId}/mods", MakeCreateRequest(isPublic: true, gameType: uniqueGameType));
        await client.PostAsJsonAsync(
            $"/organizations/{orgId}/mods", MakeCreateRequest(isPublic: true, gameType: "other-game"));

        using var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/mods/public?gameType={uniqueGameType}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<ModListItem>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(m => m.GameType == uniqueGameType);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMod_ValidRequest_Returns200WithUpdatedDetail()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        var updateRequest = new UpdateModRequest(
            Name: "Updated Name",
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: null,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var response = await client.PatchAsJsonAsync(
            $"/organizations/{TestOrgId}/mods/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ModDetail>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateMod_NotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var updateRequest = new UpdateModRequest(
            Name: "Updated Name",
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: null,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var response = await client.PatchAsJsonAsync(
            $"/organizations/{TestOrgId}/mods/{Guid.NewGuid()}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateMod_VisibilityChange_ReturnsUpdatedVisibility()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest(isPublic: false));
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        var updateRequest = new UpdateModRequest(
            Name: null,
            Description: null,
            Author: null,
            CategoryId: null,
            IsPublic: true,
            IsArchived: null,
            ProjectUrl: null,
            IconUrl: null,
            Tags: null);

        var response = await client.PatchAsJsonAsync(
            $"/organizations/{TestOrgId}/mods/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ModDetail>();
        updated!.IsPublic.Should().BeTrue();
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMod_Exists_Returns204()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        var response = await client.DeleteAsync(
            $"/organizations/{TestOrgId}/mods/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteMod_NotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);

        var response = await client.DeleteAsync(
            $"/organizations/{TestOrgId}/mods/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMod_SetsDeletedAt()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{TestOrgId}/mods", MakeCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ModDetail>();

        await client.DeleteAsync($"/organizations/{TestOrgId}/mods/{created!.Id}");

        // Verify soft delete was set by checking the database directly
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModsDbContext>();
        var mod = await db.Mods.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == created.Id);
        mod.Should().NotBeNull();
        mod!.DeletedAt.Should().NotBeNull();
    }
}
