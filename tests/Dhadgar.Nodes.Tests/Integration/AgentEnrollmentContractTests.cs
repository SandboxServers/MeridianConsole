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
        using var certificateProvider = new ClientCertificateProvider(store);
        var agent = CreateAgent(factory, store, certificateProvider);

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
        Assert.NotNull(store.StoredClientCertificate);
        Assert.NotNull(store.GetPrivateKey());
        using var agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(store.GetPrivateKey(), out _);
        using var certKey = store.StoredClientCertificate.GetECDsaPublicKey();
        Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey!.ExportSubjectPublicKeyInfo());

        // ...that chains to the CA it was given.
        Assert.NotNull(store.StoredCaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(store.StoredCaCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = store.StoredClientCertificate.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(store.StoredClientCertificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }

    [Fact]
    public async Task RealAgent_CannotReuseAnEnrollmentToken()
    {
        using var factory = CreateFactoryWithRealCa();
        var token = await CreateEnrollmentTokenAsync(factory, Guid.NewGuid());

        var firstStore = new InMemoryCertificateStore();
        using var firstProvider = new ClientCertificateProvider(firstStore);
        var secondStore = new InMemoryCertificateStore();
        using var secondProvider = new ClientCertificateProvider(secondStore);
        var first = await CreateAgent(factory, firstStore, firstProvider).EnrollAsync(token);
        var second = await CreateAgent(factory, secondStore, secondProvider).EnrollAsync(token);

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task RealAgent_EnrollsWhileNodesClockRunsAhead()
    {
        // D-S234: the Nodes CA stamps NotBefore from the host's fake clock, and the agent rejects a certificate
        // whose NotBefore is more than 5 minutes ahead of its own clock. The harness must give the agent the
        // host's clock; on the system clock this enrollment fails with [Enrollment.InvalidValidity].
        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(10));
        using var factory = CreateFactoryWithRealCa();

        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, enrolled.NodeId);
        Assert.NotNull(store.StoredClientCertificate);
    }
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactoryWithRealCa()
    {
        return AgentSeamHarness.CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: false);
    }

    private static AgentEnrollmentService CreateAgent(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        ICertificateStore store,
        ClientCertificateProvider certificateProvider)
    {
        return AgentSeamHarness.CreateEnrollmentService(
            new TestServerClientFactory(factory.Server),
            store,
            factory.Services.GetRequiredService<TimeProvider>(),
            certificateProvider);
    }
}
