using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Dhadgar.Testing.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam tests (VS-06R): Nodes serves user JWT routes and agent mTLS routes side by side, selected by path.
/// A user token never reaches the agent scheme and a client certificate never reaches the user scheme.
/// Rejections are proven at the server that answered: the Gateway's forwarder records what it forwarded.
/// </summary>
public sealed class GatewayUserJwtSeamTests : IDisposable
{
    private const string NodesAuthScheme = "NodesAuth";

    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-user-jwt-ca-{Guid.NewGuid()}");

    public GatewayUserJwtSeamTests()
    {
        Directory.CreateDirectory(_caDirectory);
    }

    public void Dispose()
    {
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
    public async Task DefaultSchemes_AreTheNodesSelector()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var provider = harness.Nodes.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Equal(NodesAuthScheme, (await provider.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal(NodesAuthScheme, (await provider.GetDefaultChallengeSchemeAsync())?.Name);
    }

    [Fact]
    public async Task UserToken_ThroughGateway_ReachesNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var orgId = Guid.NewGuid();
        using var client = harness.GatewayServer.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SeamJwt.CreateUserToken(Guid.NewGuid(), orgId));

        using var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedNodesResponse>();
        Assert.NotNull(page);
        Assert.Contains(harness.ForwardedRequests, r => r.ClusterId == "nodes" && r.Method == "GET" && r.Path == $"/organizations/{orgId}/nodes");
    }

    [Fact]
    public async Task TokenForOtherOrganization_IsRejectedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var orgId = Guid.NewGuid();
        using var client = harness.GatewayServer.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid()));

        using var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        // The Gateway forwarded the request, so the 403 came from Nodes' tenant check.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(harness.ForwardedRequests, r => r.Path == $"/organizations/{orgId}/nodes");
    }

    [Fact]
    public async Task NoToken_IsRejectedByGateway()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var orgId = Guid.NewGuid();
        using var client = harness.GatewayServer.CreateClient();

        using var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(harness.ForwardedRequests, r => r.Path == $"/organizations/{orgId}/nodes");
    }

    [Fact]
    public async Task UserToken_OnAgentEndpoint_IsRejectedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var nodeId = Guid.NewGuid();
        using var client = harness.Nodes.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid()));

        using var response = await client.PostAsJsonAsync($"/agents/{nodeId}/heartbeat", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentCertificate_OnUserRoute_IsRejectedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var orgId = Guid.NewGuid();
        var (_, store) = await harness.EnrollThroughGatewayAsync(orgId);
        using var client = ForwardedCertificateClient(harness, store);

        using var response = await client.GetAsync($"/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentCertificate_OnAgentEndpoint_IsAcceptedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(orgId);
        using var client = ForwardedCertificateClient(harness, store);

        using var response = await client.PostAsJsonAsync($"/agents/{enrolled.NodeId}/heartbeat", new { });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForeignCaCertificate_OnAgentEndpoint_IsRejectedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory, realUserAuth: true);
        using var foreign = ForeignCa.CreateClientCertificate(Guid.NewGuid());
        using var client = ForwardedCertificateClient(harness, foreign.RawData);

        using var response = await client.PostAsJsonAsync($"/agents/{Guid.NewGuid()}/heartbeat", new { });

        // Nodes validated the chain against its own CA and answered; the client never sent a request it could refuse itself.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A Nodes client whose connection comes from the Gateway address and carries the forwarded certificate.</summary>
    private static HttpClient ForwardedCertificateClient(GatewayNodesHarness harness, InMemoryCertificateStore store) =>
        ForwardedCertificateClient(harness, store.StoredClientCertificate!.RawData);

    private static HttpClient ForwardedCertificateClient(GatewayNodesHarness harness, byte[] der)
    {
        var handler = harness.Nodes.Server.CreateHandler(ctx => ctx.Connection.RemoteIpAddress = GatewayTestFactory.GatewayAddress);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://nodes.seam") };
        client.DefaultRequestHeaders.Add(AgentCertificateProfile.ForwardedCertificateHeader, Convert.ToBase64String(der));
        return client;
    }
}
