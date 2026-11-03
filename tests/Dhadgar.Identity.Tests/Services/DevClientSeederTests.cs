using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests.Services;

/// <summary>
/// Tests for <see cref="DevClientSeeder.ReconcileOrganizationAsync"/> against the real Identity host.
/// </summary>
[Collection("Identity Integration")]
public sealed class DevClientSeederTests : IAsyncLifetime
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private static readonly Guid OrgA = new("3c6e2a10-1b4d-4f7e-8a9c-0d5b7e2f4a11");
    private static readonly Guid OrgB = new("9e1d4b7c-2a6f-4e3b-b5d8-7c0a1f6e3b22");

    public DevClientSeederTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dhadgar.Identity.Data.IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Reconcile_UnboundClientToOrg_TokenCarriesOrgId()
    {
        var clientId = await CreateClientAsync(organizationId: null);

        await ReconcileAsync(clientId, OrgA);

        var jwt = await MintTokenAsync(clientId, "seed-secret");
        Assert.Equal(OrgA.ToString("D"), OrgClaim(jwt));
    }

    [Fact]
    public async Task Reconcile_BoundClientRebound_TokenCarriesNewOrgId()
    {
        var clientId = await CreateClientAsync(OrgA);

        await ReconcileAsync(clientId, OrgB);

        var jwt = await MintTokenAsync(clientId, "seed-secret");
        Assert.Equal(OrgB.ToString("D"), OrgClaim(jwt));
    }

    [Fact]
    public async Task Reconcile_BoundClientCleared_TokenHasNoOrgId()
    {
        var clientId = await CreateClientAsync(OrgA);

        await ReconcileAsync(clientId, null);

        var jwt = await MintTokenAsync(clientId, "seed-secret");
        Assert.Null(OrgClaim(jwt));
    }

    [Fact]
    public async Task Reconcile_AfterReconcile_OldSecretStillLogsIn()
    {
        // Guards against UpdateAsync re-hashing or clearing the secret (PI2 stop condition).
        var clientId = await CreateClientAsync(organizationId: null);

        await ReconcileAsync(clientId, OrgA);

        using var response = await RequestTokenAsync(clientId, "seed-secret");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reconcile_MissingClient_DoesNothing()
    {
        var clientId = $"dev-missing-{Guid.NewGuid():N}";

        await ReconcileAsync(clientId, OrgA);

        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        Assert.Null(await manager.FindByClientIdAsync(clientId));
    }

    [Theory]
    [InlineData("Production", false, false)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", true, true)]
    [InlineData("Development", false, true)]
    [InlineData("Testing", false, true)]
    public void ResolveAllowedOrganization_ConfiguredOrg_HonoursEnvironmentAndOverride(
        string environmentName, bool allowOverride, bool expectsOrg)
    {
        var resolved = DevClientSeeder.ResolveAllowedOrganization(
            OrgA, new StubHostEnvironment(environmentName), allowOverride, NullLogger.Instance);

        Assert.Equal(expectsOrg ? OrgA : null, resolved);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ResolveAllowedOrganization_NullConfigured_ReturnsNull(string environmentName)
    {
        var resolved = DevClientSeeder.ResolveAllowedOrganization(
            null, new StubHostEnvironment(environmentName), allowOverride: true, NullLogger.Instance);

        Assert.Null(resolved);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Dhadgar.Identity.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private async Task<string> CreateClientAsync(Guid? organizationId)
    {
        var clientId = $"dev-seed-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = "seed-secret",
            DisplayName = "Seeder Test Client",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "servers:read"
            }
        };
        ClientOrganizationBinding.Apply(descriptor, organizationId);
        await manager.CreateAsync(descriptor);
        return clientId;
    }

    private async Task ReconcileAsync(string clientId, Guid? organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await DevClientSeeder.ReconcileOrganizationAsync(
            manager, clientId, organizationId, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<JwtSecurityToken> MintTokenAsync(string clientId, string secret)
    {
        using var response = await RequestTokenAsync(clientId, secret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = result.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);
        return new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string clientId, string secret)
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = "servers:read"
        });

        return await _client.PostAsync("/connect/token", request);
    }

    private static string? OrgClaim(JwtSecurityToken jwt) =>
        jwt.Claims.FirstOrDefault(c => c.Type == TokenClaims.OrganizationId)?.Value;
}
