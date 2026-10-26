extern alias nodes;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Seams.Tests.Infrastructure;
using nodes::Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Seams.Tests.Enrollment;

/// <summary>
/// Seam: the real agent enrollment runs against the real Nodes behind the real Gateway.
/// </summary>
public sealed class AgentEnrollmentThroughGatewaySeamTests
{
    [Fact]
    public async Task RealAgent_EnrollsThroughGateway()
    {
        using var host = new SeamHost();
        var orgId = Guid.NewGuid();
        var token = await host.CreateEnrollmentTokenAsync(orgId);
        using var agent = host.CreateAgent();

        var result = await agent.EnrollAsync(token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(Guid.Empty, result.Value!.NodeId);
        Assert.Equal(orgId, result.Value.OrganizationId);

        using (var scope = host.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == result.Value.NodeId);
            Assert.Equal(orgId, node.OrganizationId);
            Assert.True(await db.HardwareInventories.AnyAsync(h => h.NodeId == node.Id));
            Assert.True(await db.AgentCertificates.AnyAsync(c => c.NodeId == node.Id));
        }

        var store = agent.Store;
        Assert.NotNull(store.ClientCertificate);
        Assert.NotNull(store.PrivateKey);
        using var agentKey = ECDsa.Create();
        agentKey.ImportECPrivateKey(store.PrivateKey, out _);
        using var certKey = store.ClientCertificate.GetECDsaPublicKey();
        Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey!.ExportSubjectPublicKeyInfo());

        Assert.NotNull(store.CaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(store.CaCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = store.ClientCertificate.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(store.ClientCertificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));

        Assert.Contains("nodes", host.Forwarder.ForwardedClusters);
    }
}
