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
public sealed class ServerTemplatesEndpointTests
{
    private readonly ServersWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ServerTemplatesEndpointTests(ServersWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static CreateServerTemplateRequest ValidCreateRequest(string? name = null) => new(
        Name: name ?? $"template-{Guid.NewGuid():N}"[..20],
        Description: "A test template",
        GameType: "minecraft",
        IsPublic: false,
        DefaultCpuLimitMillicores: 2000,
        DefaultMemoryLimitMb: 4096,
        DefaultDiskLimitMb: 10240,
        DefaultStartupCommand: "java -jar server.jar",
        DefaultGameSettings: null,
        DefaultEnvironmentVariables: null,
        DefaultJavaFlags: "-Xmx4G",
        DefaultPorts: null);

    private static string BaseUrl(Guid orgId) => $"/organizations/{orgId}/templates";

    // --- Create ---

    [Fact]
    public async Task CreateTemplate_ValidRequest_Returns201()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var request = ValidCreateRequest();
        var response = await client.PostAsJsonAsync(BaseUrl(orgId), request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await response.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.Name.Should().Be(request.Name);
        detail.GameType.Should().Be("minecraft");
        detail.OrganizationId.Should().Be(orgId);
        detail.IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTemplate_DuplicateName_Returns409()
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
    public async Task CreateTemplate_InvalidRequest_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var request = ValidCreateRequest() with { Name = "", GameType = "" };
        var response = await client.PostAsJsonAsync(BaseUrl(orgId), request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Get ---

    [Fact]
    public async Task GetTemplate_Exists_Returns200()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);

        var response = await client.GetAsync($"{BaseUrl(orgId)}/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetTemplate_NotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync($"{BaseUrl(orgId)}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTemplate_PrivateFromOtherOrg_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        // Create a private template in orgId
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId),
            ValidCreateRequest() with { IsPublic = false });
        var created = await createResponse.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);

        // Try to access from another org
        using var otherClient = _factory.CreateAuthenticatedClient(userId, otherOrgId);
        var response = await otherClient.GetAsync($"{BaseUrl(otherOrgId)}/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- List ---

    [Fact]
    public async Task ListTemplates_IncludesOrgTemplates()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());

        var response = await client.GetAsync($"{BaseUrl(orgId)}?includePublic=false");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    // --- List Public ---

    [Fact]
    public async Task ListPublicTemplates_NoAuth_Returns200()
    {
        // Create a public template first
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var authClient = _factory.CreateAuthenticatedClient(userId, orgId);
        await authClient.PostAsJsonAsync(BaseUrl(orgId),
            ValidCreateRequest() with { IsPublic = true });

        // Access public templates without auth
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/templates/public");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateTemplate_ValidRequest_Returns200()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);

        var updateRequest = new UpdateServerTemplateRequest(
            "updated-template", "Updated description", true, null, null, null, null, null, null, null, null, null);
        var response = await client.PatchAsJsonAsync($"{BaseUrl(orgId)}/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);
        detail!.Name.Should().Be("updated-template");
        detail.Description.Should().Be("Updated description");
        detail.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTemplate_DuplicateName_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var name1 = $"t1-{Guid.NewGuid():N}"[..14];
        var name2 = $"t2-{Guid.NewGuid():N}"[..14];
        await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest(name1));
        var createResponse2 = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest(name2));
        var created2 = await createResponse2.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);

        var updateRequest = new UpdateServerTemplateRequest(
            name1, null, null, null, null, null, null, null, null, null, null, null);
        var response = await client.PatchAsJsonAsync($"{BaseUrl(orgId)}/{created2!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteTemplate_Exists_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var createResponse = await client.PostAsJsonAsync(BaseUrl(orgId), ValidCreateRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ServerTemplateDetail>(JsonOptions);

        var response = await client.DeleteAsync($"{BaseUrl(orgId)}/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it is no longer retrievable
        var getResponse = await client.GetAsync($"{BaseUrl(orgId)}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
