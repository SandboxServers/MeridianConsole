using System.Net;
using Dhadgar.Seams.Tests.Infrastructure;

namespace Dhadgar.Seams.Tests.AgentIngress;

/// <summary>
/// Seam: agent ingress routes through the Gateway.
/// </summary>
public sealed class AgentIngressThroughGatewaySeamTests
{
    [Fact(Skip = "Pending VS-07: Gateway agents-route requires a JWT (F-04)")]
    public async Task CaCertificate_IsFetchableAnonymously()
    {
        using var host = new SeamHost();
        using var client = new HttpClient(host.GatewayHandler()) { BaseAddress = new Uri("https://localhost/") };

        using var response = await client.GetAsync("/api/v1/agents/ca-certificate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN CERTIFICATE", body);
    }
}
