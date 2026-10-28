using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L2: the lab's Nodes adapter mints an enrollment token and the product accepts the agent enrollment. Red in L01 because the adapter and probe are stubs; green in L11.
/// </summary>
public sealed class NodesEnrollSeamTests
{
    [Fact]
    [Trait("Seam", "S-L2")]
    public async Task Enroll_MintedToken_ReturnsCreatedWithNodeId()
    {
        using var factory = new LabNodesFactory();
        using var vault = new SecretVault();
        var adapter = new NodesLabAdapter(factory.CreateDbContext, vault, TimeProvider.System, NullLoggerFactory.Instance);

        var minted = await adapter.MintTokenAsync(Guid.NewGuid(), "lab-s2", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.True(minted.IsSuccess, minted.Error);

        var request = EnrollProbe.BuildRequest(vault, minted.Value.TokenRef, "linux", out _);
        Assert.True(request.IsSuccess, request.Error);

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/agents/enroll", request.Value);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<EnrollNodeResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.NodeId);
    }
}
