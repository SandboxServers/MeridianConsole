using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;
using NodesEnrollmentTokenService = Dhadgar.Nodes.Services.IEnrollmentTokenService;

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
        agentKey.ImportECPrivateKey(store.PrivateKey, out _);
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
        return _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<NodesOptions>(o =>
            {
                o.CaStoragePath = _caDirectory;
                o.CaKeySize = 2048;
            });
            services.RemoveAll<ICaStorageProvider>();
            services.RemoveAll<ICertificateAuthorityService>();
            services.AddSingleton<ICaStorageProvider, LocalFileCaStorageProvider>();
            services.AddSingleton<ICertificateAuthorityService, CertificateAuthorityService>();
        }));
    }

    private static async Task<string> CreateEnrollmentTokenAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid orgId)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<NodesEnrollmentTokenService>();
        var (_, plainToken) = await tokens.CreateTokenAsync(orgId, "user-1", "contract-test");
        return plainToken;
    }

    private static AgentEnrollmentService CreateAgent(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        ICertificateStore store)
    {
        return new AgentEnrollmentService(
            store,
            new TestServerClientFactory(factory.Server),
            Options.Create(new AgentOptions()),
            NullLogger<AgentEnrollmentService>.Instance,
            new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance));
    }

    private sealed class StripApiPrefixHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
            {
                request.RequestUri = new UriBuilder(uri) { Path = uri.AbsolutePath["/api/v1".Length..] }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Like the real IHttpClientFactory, returns a new client per call: the agent disposes
    /// the client after each request. The agent calls https://&lt;gateway&gt;/api/v1/agents/...;
    /// the Gateway strips /api/v1 (pinned by Gateway RouteConfigurationTests), reproduced here.
    /// </summary>
    private sealed class TestServerClientFactory(TestServer server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            // Ownership passes to the HttpClient (disposeHandler: true) and then to the agent,
            // which disposes the client after each request.
#pragma warning disable CA2000
            var handler = new StripApiPrefixHandler { InnerHandler = server.CreateHandler() };
#pragma warning restore CA2000
            return new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri("https://localhost/")
            };
        }
    }

    private sealed class InMemoryCertificateStore : ICertificateStore
    {
        public X509Certificate2? ClientCertificate { get; private set; }
        public byte[]? PrivateKey { get; private set; }
        public X509Certificate2? CaCertificate { get; private set; }

        public X509Certificate2? GetClientCertificate() => ClientCertificate is null
            ? null
            : X509CertificateLoader.LoadCertificate(ClientCertificate.RawData);

        public Task StoreCertificateAsync(X509Certificate2 certificate, byte[] privateKey, CancellationToken cancellationToken = default)
        {
            ClientCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
            PrivateKey = (byte[])privateKey.Clone(); // caller zeroes its copy
            return Task.CompletedTask;
        }

        public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
        {
            ClientCertificate = null;
            PrivateKey = null;
            return Task.CompletedTask;
        }

        public X509Certificate2? GetCaCertificate() => CaCertificate;

        public Task StoreCaCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
        {
            CaCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
            return Task.CompletedTask;
        }

        public bool NeedsRenewal(int thresholdDays) => false;
    }
}
