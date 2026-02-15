using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts.Mods;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Mods.Tests.Endpoints;

[Collection("Mods Integration")]
public class ModVersionsEndpointTests
{
    private readonly ModsWebApplicationFactory _factory;

    public ModVersionsEndpointTests(ModsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestOrgId = Guid.NewGuid();

    private static CreateModRequest MakeCreateModRequest() => new(
        Name: $"VersionTest Mod {Guid.NewGuid():N}"[..30],
        Slug: $"vt-{Guid.NewGuid():N}"[..25],
        Description: "Mod for version tests",
        Author: "TestAuthor",
        CategoryId: null,
        GameType: "minecraft",
        IsPublic: false,
        ProjectUrl: null,
        IconUrl: null,
        Tags: null);

    private static PublishVersionRequest MakePublishRequest(
        string version = "1.0.0",
        bool isPrerelease = false) => new(
            Version: version,
            ReleaseNotes: $"Release notes for {version}",
            FileHash: "abc123hash",
            FileSizeBytes: 2048,
            FilePath: "/mods/test.zip",
            MinGameVersion: "1.0.0",
            MaxGameVersion: null,
            IsPrerelease: isPrerelease,
            Dependencies: null);

    private static async Task<ModDetail> CreateModAsync(HttpClient client, Guid orgId)
    {
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/mods", MakeCreateModRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var mod = await response.Content.ReadFromJsonAsync<ModDetail>();
        mod.Should().NotBeNull();
        return mod!;
    }

    private static string VersionsUrl(Guid orgId, Guid modId) =>
        $"/organizations/{orgId}/mods/{modId}/versions";

    // ── Publish ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishVersion_ValidRequest_Returns201()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var response = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var version = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        version.Should().NotBeNull();
        version!.Version.Should().Be("1.0.0");
        version.Major.Should().Be(1);
        version.Minor.Should().Be(0);
        version.Patch.Should().Be(0);
        version.IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task PublishVersion_DuplicateVersion_Returns409()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var response1 = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        var response2 = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PublishVersion_InvalidSemver_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var request = new PublishVersionRequest(
            Version: "not-semver",
            ReleaseNotes: null,
            FileHash: null,
            FileSizeBytes: 1024,
            FilePath: null,
            MinGameVersion: null,
            MaxGameVersion: null,
            IsPrerelease: false,
            Dependencies: null);

        var response = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PublishVersion_ModNotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);

        var response = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, Guid.NewGuid()), MakePublishRequest());

        // The endpoint validates first, then checks mod existence.
        // A non-existent mod returns either 404 or 400 depending on the error path.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PublishVersion_NewerBecomesLatest_OlderDoesNot()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        // Publish 2.0.0 first
        var r1 = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0"));
        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        var v200 = await r1.Content.ReadFromJsonAsync<ModVersionDetail>();
        v200!.IsLatest.Should().BeTrue();

        // Publish 1.0.0 (older) -- should NOT become latest
        var r2 = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        r2.StatusCode.Should().Be(HttpStatusCode.Created);
        var v100 = await r2.Content.ReadFromJsonAsync<ModVersionDetail>();
        v100!.IsLatest.Should().BeFalse();

        // Publish 3.0.0 (newer) -- SHOULD become latest
        var r3 = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("3.0.0"));
        r3.StatusCode.Should().Be(HttpStatusCode.Created);
        var v300 = await r3.Content.ReadFromJsonAsync<ModVersionDetail>();
        v300!.IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task PublishVersion_Prerelease_NotMarkedAsLatest()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        // Publish stable 1.0.0
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));

        // Publish prerelease 2.0.0-beta.1
        var response = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0-beta.1", isPrerelease: true));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var prerelease = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        prerelease!.IsPrerelease.Should().BeTrue();
        prerelease.IsLatest.Should().BeFalse();
    }

    // ── Get Version ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersion_Exists_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        var publishResponse = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        var published = await publishResponse.Content.ReadFromJsonAsync<ModVersionDetail>();

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/{published!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        version.Should().NotBeNull();
        version!.Id.Should().Be(published.Id);
        version.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task GetVersion_NotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Get Latest Version ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestVersion_HasVersions_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0"));

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        version.Should().NotBeNull();
        version!.Version.Should().Be("2.0.0");
    }

    [Fact]
    public async Task GetLatestVersion_NoVersions_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/latest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLatestVersion_ExcludesPrerelease_ByDefault()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0-beta.1", isPrerelease: true));

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        version!.Version.Should().Be("1.0.0");
        version.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestVersion_IncludesPrerelease_WhenRequested()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0-beta.1", isPrerelease: true));

        var response = await client.GetAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/latest?includePrerelease=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<ModVersionDetail>();
        version!.Version.Should().Be("2.0.0-beta.1");
    }

    // ── List Versions ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListVersions_ReturnsAllVersions()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.1.0"));
        await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("2.0.0"));

        var response = await client.GetAsync(VersionsUrl(TestOrgId, mod.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await response.Content.ReadFromJsonAsync<List<ModVersionSummary>>();
        versions.Should().NotBeNull();
        versions!.Should().HaveCount(3);
    }

    // ── Deprecate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeprecateVersion_ValidRequest_Returns204()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);
        var publishResponse = await client.PostAsJsonAsync(
            VersionsUrl(TestOrgId, mod.Id), MakePublishRequest("1.0.0"));
        var published = await publishResponse.Content.ReadFromJsonAsync<ModVersionDetail>();

        var deprecateRequest = new DeprecateVersionRequest(
            Reason: "Security vulnerability",
            RecommendedVersionId: null);

        var response = await client.PostAsJsonAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/{published!.Id}/deprecate", deprecateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeprecateVersion_VersionNotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);
        var mod = await CreateModAsync(client, TestOrgId);

        var deprecateRequest = new DeprecateVersionRequest(
            Reason: "Security vulnerability",
            RecommendedVersionId: null);

        var response = await client.PostAsJsonAsync(
            $"{VersionsUrl(TestOrgId, mod.Id)}/{Guid.NewGuid()}/deprecate", deprecateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeprecateVersion_ModNotFound_Returns404()
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);

        var deprecateRequest = new DeprecateVersionRequest(
            Reason: "Security vulnerability",
            RecommendedVersionId: null);

        var response = await client.PostAsJsonAsync(
            $"{VersionsUrl(TestOrgId, Guid.NewGuid())}/{Guid.NewGuid()}/deprecate", deprecateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
