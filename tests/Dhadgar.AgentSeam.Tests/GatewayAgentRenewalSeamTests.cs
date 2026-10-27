using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam tests (S3, through the Gateway): the real agent renews its certificate at
/// <c>/api/v1/agents/{nodeId}/certificates/renew</c> on the Gateway, which forwards to the real Nodes host. The old
/// certificate stays usable inside the renewal grace window and the hub accepts it there. The time-advance and
/// lost-response cases run only in the direct test (Nodes.Tests). Red until Nodes P09 and P16 land.
/// </summary>
public sealed class GatewayAgentRenewalSeamTests : IDisposable
{
    private static readonly Uri NegotiateUri = new("https://localhost/hubs/agent/negotiate?negotiateVersion=1");

    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-agent-renewal-seam-ca-{Guid.NewGuid()}");
    private readonly List<byte[]> _keyCopies = [];

    public GatewayAgentRenewalSeamTests()
    {
        Directory.CreateDirectory(_caDirectory);
    }

    public void Dispose()
    {
        foreach (var key in _keyCopies)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _keyCopies.Clear();

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
    public async Task RealAgent_RenewsThroughGateway_OldCertificateStillUsableInGrace()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());

        var oldThumbprint = DbThumbprint(store.ClientCertificate!);
        var oldSpki = PublicKeySpki(store.ClientCertificate!);
        using var oldCertificate = X509CertificateLoader.LoadCertificate(store.ClientCertificate!.RawData);

        // A second store holds the old certificate and key for the old-certificate ControlPlaneClient.
        var oldStore = new InMemoryCertificateStore();
        await oldStore.StoreCertificateAsync(store.ClientCertificate!, TrackKey(store.PrivateKey!));

        // The agent's mTLS client reaches the Gateway with the current certificate on the TLS connection.
        var renewing = CreateRenewingAgent(
            new TestServerClientFactory(harness.GatewayServer, stripApiPrefix: false, certificate: () => store.ClientCertificate),
            store,
            enrolled,
            harness.NodesBase.TimeProvider);
        var renewal = await renewing.RenewCertificateAsync();
        Assert.True(renewal.IsSuccess, renewal.Error);

        var newCertificate = store.ClientCertificate!;
        var newSpki = PublicKeySpki(newCertificate);
        Assert.NotEqual(oldThumbprint, DbThumbprint(newCertificate));
        Assert.Equal(PrivateKeySpki(store), newSpki);
        Assert.NotEqual(oldSpki, newSpki);
        AssertChainsToStoredCa(store);

        using (var scope = harness.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var rows = await db.AgentCertificates.AsNoTracking().Where(c => c.NodeId == enrolled.NodeId).ToListAsync();
            Assert.Contains(rows, r => r.Thumbprint == oldThumbprint && r.IsRevoked);
            Assert.Contains(rows, r => r.Thumbprint == DbThumbprint(newCertificate) && !r.IsRevoked);
        }

        // Hub negotiate through the Gateway: the new certificate and the old one (inside the window) get 200.
        Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(harness.GatewayServer, newCertificate));
        Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(harness.GatewayServer, oldCertificate));

        // A real client on the old certificate connects through the Gateway and is acknowledged inside the window.
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var oldClient = CreateAgentClient(harness.GatewayServer, enrolled, oldStore, meter, activity);
        await ConnectAgentAsync(oldClient);
        await SendAgentHeartbeatAsync(oldClient, new HeartbeatPayload
        {
            NodeId = enrolled.NodeId,
            AgentVersion = "2.0.0-renewal"
        });
    }

    /// <summary>
    /// Copies a private key the test must zero later. The copy is owned by the test and zeroed in <see cref="Dispose"/>.
    /// </summary>
    private byte[] TrackKey(byte[] key)
    {
        var copy = (byte[])key.Clone();
        _keyCopies.Add(copy);
        return copy;
    }

    /// <summary>
    /// Builds the real agent enrollment service with the enrolled node identity, so renewal is not refused as
    /// "not enrolled". P11 changes this to the identity provider.
    /// </summary>
    private static AgentEnrollmentService CreateRenewingAgent(
        IHttpClientFactory httpClientFactory,
        ICertificateStore store,
        EnrollmentResult enrolled,
        TimeProvider timeProvider)
    {
        var identity = new AgentIdentityProvider();
        identity.Set(new AgentIdentity(enrolled.NodeId, enrolled.OrganizationId, DateTimeOffset.UtcNow));

        return new AgentEnrollmentService(
            store,
            httpClientFactory,
            Options.Create(new AgentOptions()),
            NullLogger<AgentEnrollmentService>.Instance,
            new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance),
            new ClientCertificateProvider(store),
            identity,
            enrollmentTokenCleanup: null,
            timeProvider: timeProvider);
    }

    /// <summary>
    /// The thumbprint format Nodes stores in <c>AgentCertificate.Thumbprint</c>: lowercase SHA-256 hex of the DER bytes.
    /// <see cref="X509Certificate2.Thumbprint"/> is SHA-1 and uppercase, so it never matches a stored row.
    /// </summary>
    private static string DbThumbprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    private static byte[] PublicKeySpki(X509Certificate2 certificate)
    {
        using var key = certificate.GetECDsaPublicKey();
        Assert.NotNull(key);
        return key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The public key of the private key the store holds (not the certificate's own key).
    /// </summary>
    private static byte[] PrivateKeySpki(InMemoryCertificateStore store)
    {
        using var withKey = store.GetClientCertificate();
        Assert.NotNull(withKey);
        using var key = withKey.GetECDsaPrivateKey();
        Assert.NotNull(key);
        return key.ExportSubjectPublicKeyInfo();
    }

    private static void AssertChainsToStoredCa(InMemoryCertificateStore store)
    {
        Assert.NotNull(store.CaCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(store.CaCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = store.ClientCertificate!.NotBefore.AddMinutes(1);
        Assert.True(chain.Build(store.ClientCertificate), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }

    /// <summary>
    /// Posts to the hub negotiate endpoint on the Gateway, presenting <paramref name="certificate"/> on the TLS connection.
    /// </summary>
    private static async Task<HttpStatusCode> NegotiateWithCertificateAsync(TestServer gatewayServer, X509Certificate2 certificate)
    {
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        using var http = new HttpClient(CertificateInjectingHandlerFactory(gatewayServer)(handler));

        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiateUri)
        {
            Content = new ByteArrayContent([])
        };
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// Connect step. <see cref="IControlPlaneClient.ConnectAsync"/> returns a <c>Result</c>; a refused connection fails here.
    /// </summary>
    private static async Task ConnectAgentAsync(IControlPlaneClient client)
    {
        var result = await client.ConnectAsync();
        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// Heartbeat step. The payload is mapped to the typed contract, and the ack must be accepted.
    /// </summary>
    private static async Task SendAgentHeartbeatAsync(IControlPlaneClient client, HeartbeatPayload payload)
    {
        var result = await client.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(payload));
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.Acknowledged, result.Value.ErrorCode);
    }

    /// <summary>
    /// Builds a real <see cref="ControlPlaneClient"/> whose hub calls go to the Gateway test server.
    /// Copied from <c>GatewayAgentHubSeamTests</c>, where it is private.
    /// </summary>
    private static ControlPlaneClient CreateAgentClient(
        TestServer gatewayServer,
        EnrollmentResult enrolled,
        InMemoryCertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity)
    {
        var identity = new AgentIdentityProvider();
        identity.Set(new AgentIdentity(enrolled.NodeId, enrolled.OrganizationId, DateTimeOffset.UtcNow));

        return new ControlPlaneClient(
            Options.Create(new AgentOptions
            {
                ControlPlane = { Endpoint = "https://localhost" }
            }),
            store,
            meter,
            activity,
            NullLogger<ControlPlaneClient>.Instance,
            identity,
            new ControlPlaneHubTransport
            {
                HttpMessageHandlerFactory = CertificateInjectingHandlerFactory(gatewayServer),
                Transports = HttpTransportType.LongPolling
            });
    }
}
