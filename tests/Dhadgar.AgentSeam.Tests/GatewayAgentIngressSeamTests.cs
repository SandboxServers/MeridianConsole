using System.Net;
using System.Net.Http;
using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam tests (VS-07R): which agent-facing paths the Gateway serves without a client certificate. The CA certificate
/// is public (Nodes serves it anonymously); every other <c>/api/v1/agents/*</c> path still requires the agent's
/// certificate at the Gateway. Rejections are proven at the Gateway: the forwarder recorded no request for them.
/// </summary>
public sealed class GatewayAgentIngressSeamTests : IDisposable
{
    private const string CaCertificatePath = "/api/v1/agents/ca-certificate";

    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-agent-ingress-ca-{Guid.NewGuid()}");

    public GatewayAgentIngressSeamTests()
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
    public async Task CaCertificate_IsFetchableAnonymously()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        using var client = harness.GatewayServer.CreateClient();

        using var response = await client.GetAsync(CaCertificatePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-pem-file", response.Content.Headers.ContentType?.MediaType);
        var pem = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem, StringComparison.Ordinal);
        Assert.Contains(new ForwardedRequest("nodes", "GET", "/agents/ca-certificate"), harness.ForwardedRequests);
    }

    [Fact]
    public async Task RenewEndpoint_WithoutCertificate_IsRejectedByGateway()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        using var client = harness.GatewayServer.CreateClient();
        var nodeId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agents/{nodeId:D}/certificates/renew")
        {
            Content = new ByteArrayContent([])
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(harness.ForwardedRequests, r => r.Path.EndsWith("/certificates/renew", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenewEndpoint_WithCertificate_IsForwardedByGateway()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(store.StoredClientCertificate!);
        using var http = new HttpClient(CertificateInjectingHandlerFactory(harness.GatewayServer)(handler));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://localhost/api/v1/agents/{enrolled.NodeId:D}/certificates/renew")
        {
            Content = new ByteArrayContent([])
        };

        using var response = await http.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(harness.ForwardedRequests, r => r.ClusterId == "nodes" && r.Method == "POST" && r.Path == $"/agents/{enrolled.NodeId:D}/certificates/renew");
    }

    [Fact]
    public async Task CaCertificatePathVariant_IsNotAnonymous()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        using var client = harness.GatewayServer.CreateClient();

        using var response = await client.GetAsync($"{CaCertificatePath}/x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(harness.ForwardedRequests, r => r.Path.Contains("ca-certificate", StringComparison.Ordinal));
    }
}
