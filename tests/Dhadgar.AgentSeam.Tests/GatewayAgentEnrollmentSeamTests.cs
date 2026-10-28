using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam: the real agent enrollment runs through the real Gateway into the real Nodes. Beyond success, the enrollment
/// leaves the node, inventory and certificate rows, a certificate for the agent's own key, and a chain to the CA the
/// agent stored. Moved from tests/Dhadgar.Seams.Tests (D-42).
/// </summary>
public sealed class GatewayAgentEnrollmentSeamTests : IDisposable
{
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-agent-enroll-seam-ca-{Guid.NewGuid()}");

    public GatewayAgentEnrollmentSeamTests()
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
    public async Task RealAgent_EnrollsThroughGateway_PersistsNodeAndIssuesChainedCertificateForAgentKey()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(orgId);

        Assert.NotEqual(Guid.Empty, enrolled.NodeId);
        Assert.Equal(orgId, enrolled.OrganizationId);

        using (var scope = harness.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            Assert.Equal(orgId, node.OrganizationId);
            Assert.True(await db.HardwareInventories.AnyAsync(h => h.NodeId == node.Id));
            Assert.True(await db.AgentCertificates.AnyAsync(c => c.NodeId == node.Id));
        }

        var certificate = Assert.IsType<X509Certificate2>(store.StoredClientCertificate);
        var privateKey = Assert.IsType<byte[]>(store.GetPrivateKey());
        try
        {
            using var agentKey = ECDsa.Create();
            agentKey.ImportPkcs8PrivateKey(privateKey, out _);
            using var certKey = certificate.GetECDsaPublicKey();
            Assert.NotNull(certKey);
            Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }

        var ca = Assert.IsType<X509Certificate2>(store.StoredCaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = certificate.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(certificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }
}
