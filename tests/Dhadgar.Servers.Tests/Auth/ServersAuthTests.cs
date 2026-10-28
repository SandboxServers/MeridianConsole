using System.Net;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Servers.Tests.Auth;

[Collection("Servers Integration")]
public sealed class ServersAuthTests
{
    private readonly ServersWebApplicationFactory _factory;

    public ServersAuthTests(ServersWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string ServersUrl(Guid orgId) => $"/organizations/{orgId}/servers";

    [Fact]
    public async Task ListServers_WithoutCredentials_Returns401()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ServersUrl(orgId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListServers_IdentityForAnotherOrg_Returns403()
    {
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid(), otherOrgId);

        var response = await client.GetAsync(ServersUrl(orgId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListServers_IdentityForSameOrg_Returns200()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid(), orgId);

        var response = await client.GetAsync(ServersUrl(orgId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
