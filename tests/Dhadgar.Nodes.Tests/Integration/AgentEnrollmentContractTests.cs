using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;
using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;

namespace Dhadgar.Nodes.Tests.Integration;

/// <summary>
/// Drives the real agent <see cref="AgentEnrollmentService"/> against the real Nodes API and
/// real certificate authority. This is the seam that was broken (DV-1 in PROJECT-STATE.md):
/// the two sides were built from different plans and no call ever succeeded.
/// </summary>
[Collection("Nodes Integration")]
public sealed class AgentEnrollmentContractTests : IDisposable
{
    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-contract-ca-{Guid.NewGuid()}");

    public AgentEnrollmentContractTests(NodesWebApplicationFactory factory)
    {
        _baseFactory = factory;
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
    public async Task RealAgent_EnrollsAgainstRealNodes_AndHoldsAWorkingClientCertificate()
    {
        using var factory = CreateFactoryWithRealCa();
        var orgId = Guid.NewGuid();
        var token = await CreateEnrollmentTokenAsync(factory, orgId);
        var store = new InMemoryCertificateStore();
        var agent = CreateAgent(factory, store);

        var result = await agent.EnrollAsync(token);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(Guid.Empty, result.Value!.NodeId);
        Assert.Equal(orgId, result.Value.OrganizationId);

        // Control plane recorded the node under the token's org, with hardware from the real collector
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == result.Value.NodeId);
            Assert.Equal(orgId, node.OrganizationId);
            Assert.True(await db.HardwareInventories.AnyAsync(h => h.NodeId == node.Id));
            Assert.True(await db.AgentCertificates.AnyAsync(c => c.NodeId == node.Id));
        }

        // Agent holds a client cert bound to the private key it generated locally...
        Assert.NotNull(store.ClientCertificate);
        Assert.NotNull(store.PrivateKey);
        using var agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(store.PrivateKey, out _);
        using var certKey = store.ClientCertificate.GetECDsaPublicKey();
        Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey!.ExportSubjectPublicKeyInfo());

        // ...that chains to the CA it was given.
        Assert.NotNull(store.CaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(store.CaCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = store.ClientCertificate.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(store.ClientCertificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }

    [Fact]
    public async Task RealAgent_CannotReuseAnEnrollmentToken()
    {
        using var factory = CreateFactoryWithRealCa();
        var token = await CreateEnrollmentTokenAsync(factory, Guid.NewGuid());

        var first = await CreateAgent(factory, new InMemoryCertificateStore()).EnrollAsync(token);
        var second = await CreateAgent(factory, new InMemoryCertificateStore()).EnrollAsync(token);

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactoryWithRealCa()
    {
        return AgentSeamHarness.CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: false);
    }

    private static AgentEnrollmentService CreateAgent(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        ICertificateStore store)
    {
        return AgentSeamHarness.CreateEnrollmentService(new TestServerClientFactory(factory.Server), store);
    }
}
