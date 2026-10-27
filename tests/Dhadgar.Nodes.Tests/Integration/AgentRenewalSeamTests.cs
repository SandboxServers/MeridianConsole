using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;
using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.Nodes.Tests.Integration;

/// <summary>
/// Seam tests (S3): the real agent <see cref="EnrollmentService"/> renews its certificate against the real Nodes host
/// over a TestServer, with mTLS enabled and a certificate issued by the real CA. The old certificate stays usable
/// inside the renewal grace window (D-S212), and the hub accepts it there. Red until Nodes P09 and P16 land.
/// </summary>
[Collection("Nodes Integration")]
public sealed class AgentRenewalSeamTests : IDisposable
{
    private static readonly Uri NegotiateUri = new("https://localhost/hubs/agent/negotiate?negotiateVersion=1");

    private static readonly IReadOnlyDictionary<string, string?> GraceSettings =
        new Dictionary<string, string?> { ["Mtls:RenewalGraceMinutes"] = "60" };

    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-renewal-seam-ca-{Guid.NewGuid()}");
    private readonly List<byte[]> _keyCopies = [];

    public AgentRenewalSeamTests(NodesWebApplicationFactory factory)
    {
        _baseFactory = factory;
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
    public async Task RealAgent_RenewsWithFreshAgentHeldKey()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true, settings: GraceSettings);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());

        // Capture the enrolled identity before any renewal.
        var oldThumbprint = DbThumbprint(store.ClientCertificate!);
        var oldSpki = PublicKeySpki(store.ClientCertificate!);
        var oldPrivateKey = TrackKey(store.PrivateKey!);

        // A second store holds the old certificate and key, so the old certificate can keep talking to the hub.
        var oldStore = new InMemoryCertificateStore();
        await oldStore.StoreCertificateAsync(store.ClientCertificate!, store.PrivateKey!);

        var renewing = CreateRenewingAgent(
            new TestServerClientFactory(factory.Server, certificate: () => store.ClientCertificate),
            store,
            enrolled,
            _baseFactory.TimeProvider);
        var renewal = await renewing.RenewCertificateAsync();
        Assert.True(renewal.IsSuccess, renewal.Error);

        var newCertificate = store.ClientCertificate!;
        var newSpki = PublicKeySpki(newCertificate);
        Assert.NotEqual(oldThumbprint, DbThumbprint(newCertificate));
        Assert.Equal(PrivateKeySpki(store), newSpki);
        Assert.NotEqual(oldSpki, newSpki);
        AssertChainsToStoredCa(store);

        var newPrivateKey = TrackKey(store.PrivateKey!);
        Assert.False(newPrivateKey.AsSpan().SequenceEqual(oldPrivateKey));

        var rows = await ReadCertificateStateAsync(factory, enrolled.NodeId);
        Assert.Contains(rows, r => r.Thumbprint == oldThumbprint && r.IsRevoked);
        Assert.Contains(rows, r => r.Thumbprint == DbThumbprint(newCertificate) && !r.IsRevoked);

        // Hub negotiate: the new certificate and the old one (inside the grace window) both get 200.
        Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(factory, newCertificate));
        Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(factory, oldStore.ClientCertificate!));

        // A real client on the old certificate connects and is acknowledged inside the window. Only this test
        // proves the hub itself accepts a certificate in grace, not just MtlsMiddleware.
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using (var oldClient = CreateAgentClient(factory, enrolled, oldStore, meter, activity))
        {
            await ConnectAgentAsync(oldClient);
            await SendAgentHeartbeatAsync(oldClient, new HeartbeatPayload
            {
                NodeId = enrolled.NodeId,
                AgentVersion = "2.0.0-renewal"
            });

            // 61 minutes after renewal the grace window has closed: the old certificate is refused,
            // the new one still works, and the old-certificate client is no longer acknowledged.
            _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(61));
            Assert.Equal(HttpStatusCode.Unauthorized, await NegotiateWithCertificateAsync(factory, oldStore.ClientCertificate!));
            Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(factory, newCertificate));

            var expired = await oldClient.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(new HeartbeatPayload
            {
                NodeId = enrolled.NodeId,
                AgentVersion = "2.0.0-renewal"
            }));
            Assert.False(expired.IsSuccess && expired.Value.Acknowledged);
        }
    }

    [Fact]
    public async Task RealAgent_RenewalResponseLost_RenewsAgainWithOldCertificateWithinGrace()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true, settings: GraceSettings);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        var oldThumbprint = DbThumbprint(store.ClientCertificate!);
        using var oldCertificate = X509CertificateLoader.LoadCertificate(store.ClientCertificate!.RawData);

        // Only the first renewal client gets the dropping layer, so only the first renewal request is dropped.
        ResponseDroppingHandler? dropper = null;
        var renewing = CreateRenewingAgent(
            new TestServerClientFactory(
                factory.Server,
                certificate: () => store.ClientCertificate,
                wrapMtlsHandler: inner =>
                {
                    if (dropper is not null)
                    {
                        return inner;
                    }

                    dropper = new ResponseDroppingHandler { InnerHandler = inner };
                    return dropper;
                }),
            store,
            enrolled,
            _baseFactory.TimeProvider);

        // Nodes commits the new certificate, but the response is lost, so the agent keeps the old one.
        var first = await renewing.RenewCertificateAsync();
        Assert.False(first.IsSuccess);
        Assert.StartsWith("[Renewal.NetworkError]", first.Error);
        Assert.NotNull(dropper);
        Assert.Equal(1, dropper.DroppedCount);
        Assert.Equal(oldThumbprint, DbThumbprint(store.ClientCertificate!));

        var orphan = (await ReadCertificateStateAsync(factory, enrolled.NodeId))
            .Where(r => r.Thumbprint != oldThumbprint && !r.IsRevoked)
            .ToList();
        Assert.Single(orphan);
        var orphanThumbprint = orphan[0].Thumbprint;

        // Retry after 5 minutes (AgentCertificateRenewalPolicy.AgentRetryMinutes once P16 exists).
        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        var second = await renewing.RenewCertificateAsync();
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(1, dropper.DroppedCount);

        var newThumbprint = DbThumbprint(store.ClientCertificate!);
        Assert.NotEqual(oldThumbprint, newThumbprint);
        Assert.NotEqual(orphanThumbprint, newThumbprint);

        var rows = await ReadCertificateStateAsync(factory, enrolled.NodeId);
        var orphanRow = rows.Single(r => r.Thumbprint == orphanThumbprint);
        Assert.True(orphanRow.IsRevoked);
        Assert.Null(orphanRow.GraceUntil);

        // The old certificate is still usable inside the window that started at the first renewal.
        Assert.Equal(HttpStatusCode.OK, await NegotiateWithCertificateAsync(factory, oldCertificate));

        // Grace is not extended by the retry: 61 minutes after the first renewal the old certificate is refused.
        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(56));
        Assert.Equal(HttpStatusCode.Unauthorized, await NegotiateWithCertificateAsync(factory, oldCertificate));
    }

    [Fact]
    public async Task RealAgent_HubReconnectAfterRenewal_PresentsRenewedCertificate()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true, settings: GraceSettings);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());

        // One client instance for the whole test. ConnectAsync reads the store on every connect (D-S231).
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateAgentClient(factory, enrolled, store, meter, activity);
        await ConnectAgentAsync(client);

        var renewing = CreateRenewingAgent(
            new TestServerClientFactory(factory.Server, certificate: () => store.ClientCertificate),
            store,
            enrolled,
            _baseFactory.TimeProvider);
        var renewal = await renewing.RenewCertificateAsync();
        Assert.True(renewal.IsSuccess, renewal.Error);

        // AgentBootstrapService disconnects after renewal. The next ConnectAsync rebuilds the hub connection
        // from the store, so the renewed certificate is the one presented.
        await client.DisconnectAsync();
        await ConnectAgentAsync(client);

        // 61 minutes after renewal the old certificate has left grace. The renewed connection is still acknowledged.
        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(61));
        var ack = await client.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(new HeartbeatPayload
        {
            NodeId = enrolled.NodeId,
            AgentVersion = "2.0.0-renewal"
        }));
        Assert.True(ack.IsSuccess && ack.Value.Acknowledged, ack.Error);
    }

    [Fact]
    public async Task Renew_WithoutCertificate_Fails()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true, settings: GraceSettings);
        var (enrolled, _) = await EnrollAgentAsync(factory, Guid.NewGuid());
        var before = await ReadCertificateStateAsync(factory, enrolled.NodeId);

        var empty = new InMemoryCertificateStore();
        var renewing = CreateRenewingAgent(new TestServerClientFactory(factory.Server), empty, enrolled, _baseFactory.TimeProvider);

        var result = await renewing.RenewCertificateAsync();

        Assert.False(result.IsSuccess);
        Assert.StartsWith("[Renewal.NoCertificate]", result.Error);
        var after = await ReadCertificateStateAsync(factory, enrolled.NodeId);
        Assert.Equal(
            before.Select(r => r.Thumbprint).OrderBy(t => t, StringComparer.Ordinal),
            after.Select(r => r.Thumbprint).OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// Snapshot of one <c>AgentCertificate</c> row.
    /// </summary>
    private sealed record CertificateRow(string Thumbprint, bool IsRevoked, DateTime? GraceUntil);

    private static async Task<List<CertificateRow>> ReadCertificateStateAsync(
        WebApplicationFactory<Program> factory,
        Guid nodeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var rows = await db.AgentCertificates.Where(c => c.NodeId == nodeId).ToListAsync();
        return rows.Select(row => new CertificateRow(row.Thumbprint, row.IsRevoked, row.GraceUntil)).ToList();
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
        return new AgentEnrollmentService(
            store,
            httpClientFactory,
            Options.Create(new AgentOptions
            {
                NodeId = enrolled.NodeId,
                OrganizationId = enrolled.OrganizationId
            }),
            NullLogger<AgentEnrollmentService>.Instance,
            new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance),
            new ClientCertificateProvider(store),
            enrollmentTokenCleanup: null,
            timeProvider: timeProvider);
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
    /// Posts to the hub negotiate endpoint presenting <paramref name="certificate"/> on the TLS connection.
    /// </summary>
    private static async Task<HttpStatusCode> NegotiateWithCertificateAsync(
        WebApplicationFactory<Program> factory,
        X509Certificate2 certificate)
    {
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        using var http = new HttpClient(CertificateInjectingHandlerFactory(factory.Server)(handler));

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

    private static ControlPlaneClient CreateAgentClient(
        WebApplicationFactory<Program> factory,
        EnrollmentResult enrolled,
        InMemoryCertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity)
    {
        return new ControlPlaneClient(
            Options.Create(new AgentOptions
            {
                NodeId = enrolled.NodeId,
                OrganizationId = enrolled.OrganizationId,
                ControlPlane = { Endpoint = "https://localhost" }
            }),
            store,
            meter,
            activity,
            NullLogger<ControlPlaneClient>.Instance,
            new ControlPlaneHubTransport
            {
                HttpMessageHandlerFactory = CertificateInjectingHandlerFactory(factory.Server),
                Transports = HttpTransportType.LongPolling
            });
    }
}
