extern alias identity;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Contracts.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using identity::Dhadgar.Identity.Services;
using Xunit;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam tests (PI1): a client_credentials token minted by the real Identity host is sent through the real Gateway to real Nodes.
/// Identity does not issue <c>org_id</c> yet (PI2 does), so <see cref="BoundServiceToken_ThroughGateway_ReachesNodes"/>
/// is the red seam: it fails with 403 until Identity puts the bound organization on the token.
/// Tokens and secrets are never logged or printed; failed assertions show status codes only.
/// </summary>
public sealed class IdentityClientCredentialsSeamTests : IAsyncLifetime, IDisposable
{
    private const string BoundClientId = "seam-bound";
    private const string UnboundClientId = "seam-unbound";
    private const string Scope = "nodes:manage";

    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private readonly string _boundSecret = RandomNumberGenerator.GetHexString(32);
    private readonly string _unboundSecret = RandomNumberGenerator.GetHexString(32);
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"identity-client-ca-{Guid.NewGuid()}");

    private IdentitySeamFactory? _identity;
    private GatewayNodesHarness? _harness;
    private string _boundToken = string.Empty;
    private string _unboundToken = string.Empty;
    private bool _disposed;

    private GatewayNodesHarness Harness => _harness ?? throw new InvalidOperationException("InitializeAsync has not run.");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_caDirectory);
        _identity = new IdentitySeamFactory();
        await _identity.EnsureDatabaseAsync();
        await CreateApplicationsAsync(_identity);

        using var identityClient = _identity.CreateClient();
        _boundToken = await RequestTokenAsync(identityClient, BoundClientId, _boundSecret);
        _unboundToken = await RequestTokenAsync(identityClient, UnboundClientId, _unboundSecret);

        using var discovery = JsonDocument.Parse(await identityClient.GetStringAsync("/.well-known/openid-configuration"));
        var issuer = discovery.RootElement.GetProperty("issuer").GetString()
            ?? throw new InvalidOperationException("Discovery document has no issuer.");
        var keySet = new JsonWebKeySet(await identityClient.GetStringAsync("/.well-known/jwks.json"));

        _harness = new GatewayNodesHarness(
            _caDirectory,
            realUserAuth: true,
            userJwt: services => ConfigureUserJwt(services, issuer, keySet));
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _harness?.Dispose();
        _identity?.Dispose();
        try
        {
            Directory.Delete(_caDirectory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task BoundServiceToken_ThroughGateway_ReachesNodes()
    {
        using var client = Harness.GatewayServer.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _boundToken);

        using var response = await client.GetAsync($"/api/v1/organizations/{_orgA}/nodes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organizationClaim = new JwtSecurityTokenHandler()
            .ReadJwtToken(_boundToken)
            .Claims.FirstOrDefault(claim => claim.Type == TokenClaims.OrganizationId)?.Value;
        Assert.Equal(_orgA.ToString("D"), organizationClaim);
    }

    [Fact]
    public async Task BoundServiceToken_OtherOrganization_IsForbidden()
    {
        using var client = Harness.GatewayServer.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _boundToken);

        using var response = await client.GetAsync($"/api/v1/organizations/{_orgB}/nodes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnboundServiceToken_ThroughGateway_IsForbidden()
    {
        using var client = Harness.GatewayServer.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _unboundToken);

        using var response = await client.GetAsync($"/api/v1/organizations/{_orgA}/nodes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task CreateApplicationsAsync(IdentitySeamFactory identity)
    {
        using var scope = identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var bound = NewDescriptor(BoundClientId, _boundSecret);
        ClientOrganizationBinding.Apply(bound, _orgA);
        await manager.CreateAsync(bound);

        await manager.CreateAsync(NewDescriptor(UnboundClientId, _unboundSecret));
    }

    private static OpenIddictApplicationDescriptor NewDescriptor(string clientId, string clientSecret) => new()
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Permissions =
        {
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            OpenIddictConstants.Permissions.Prefixes.Scope + Scope,
        },
    };

    private static async Task<string> RequestTokenAsync(HttpClient client, string clientId, string clientSecret)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = Scope,
        });

        using var response = await client.PostAsync("/connect/token", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response has no access_token.");
    }

    /// <summary>
    /// Makes the JWT bearer scheme accept tokens from the Identity host under test, using its discovered issuer and
    /// its published signing keys. Runs on Nodes and the Gateway through the harness.
    /// </summary>
    private static void ConfigureUserJwt(IServiceCollection services, string issuer, JsonWebKeySet keySet)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.RequireHttpsMetadata = false;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration());
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = IdentitySeamFactory.TokenAudience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keySet.GetSigningKeys(),
                ClockSkew = TimeSpan.FromSeconds(5)
            };
        });
    }
}
