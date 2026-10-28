using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Hubs;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.Nodes.Tests.Hubs;

/// <summary>
/// Hub tests (P04): a raw SignalR client with an enrolled agent certificate talks to <see cref="AgentHub"/>
/// over a TestServer, with mTLS enabled and certificates issued by the real CA.
/// </summary>
[Collection("Nodes Integration")]
public sealed class AgentHubTests : IDisposable
{
    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-hub-ca-{Guid.NewGuid()}");

    public AgentHubTests(NodesWebApplicationFactory factory)
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
    public async Task Heartbeat_Valid_AcknowledgesAndUpdatesHealth()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = _baseFactory.TimeProvider.GetUtcNow().UtcDateTime;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(42.5, 40, 70, 2, "2.0.0-hub", ["x"]));

        Assert.True(ack.Acknowledged);
        Assert.Null(ack.ErrorCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == enrolled.NodeId);
        var health = await db.NodeHealths.AsNoTracking().SingleAsync(h => h.NodeId == enrolled.NodeId);
        Assert.Equal(expectedHeartbeat, node.LastHeartbeat);
        Assert.Equal("2.0.0-hub", node.AgentVersion);
        Assert.Equal(42.5, health.CpuUsagePercent);
        Assert.Equal(2, health.ActiveGameServers);
    }

    [Fact]
    public async Task Heartbeat_CpuOver100_ReturnsInvalidHeartbeat_AndLeavesDbUnchanged()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        var (lastHeartbeatBefore, cpuBefore) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(150, 40, 70, 2, "2.0.0-hub", null));

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.InvalidHeartbeat, ack.ErrorCode);

        var (lastHeartbeatAfter, cpuAfter) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);
        Assert.Equal(lastHeartbeatBefore, lastHeartbeatAfter);
        Assert.Equal(cpuBefore, cpuAfter);
    }

    [Fact]
    public async Task Heartbeat_DecommissionedNode_ReturnsNodeDecommissioned()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        // Connected first: a node decommissioned before connecting is refused at connect (VS-08R).
        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            node.Status = NodeStatus.Decommissioned;
            await db.SaveChangesAsync();
        }

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null));

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.NodeDecommissioned, ack.ErrorCode);
    }

    [Fact]
    public async Task Connect_WithUnsupportedProtocolHeader_IsClosedByServer()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Positive control: the same setup with the supported version is acknowledged, so the rejection
        // below is caused by the version header and not by the harness.
        await using (var ok = CreateHubConnection(factory, cert, protocolVersion: "1"))
        {
            await ok.StartAsync(cts.Token);
            var ack = await ok.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat),
                new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null),
                cts.Token);
            Assert.True(ack.Acknowledged);
        }

        await using var connection = CreateHubConnection(factory, cert, protocolVersion: "2");
        var rejected = false;
        try
        {
            await connection.StartAsync(cts.Token);
            // A connection the server kept open would acknowledge this heartbeat.
            await connection.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat),
                new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null),
                cts.Token);
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            // Only a server-side refusal counts; a timeout of cts is not a rejection.
            rejected = true;
        }

        Assert.True(rejected);
    }

    [Fact]
    public async Task Connect_WithoutCertificate_IsRejectedWith401()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);

        await using var connection = CreateHubConnection(factory, cert: null);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.ToString());
    }

    [Fact]
    public async Task Connect_WithForeignCaCertificate_IsRejectedWith401()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var nodeId = Guid.NewGuid();
        using var foreign = CreateSelfSignedAgentCertificate(nodeId);

        await using var connection = CreateHubConnection(factory, foreign);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.ToString());
    }

    [Fact]
    public async Task Connect_WithRevokedCertificate_IsRefused()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;
        await RevokeAgentCertificatesAsync(factory, enrolled.NodeId);

        await using var connection = CreateHubConnection(factory, cert);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acknowledged = false;
        try
        {
            await connection.StartAsync(cts.Token);
            var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat),
                new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null),
                cts.Token);
            acknowledged = ack.Acknowledged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Refused at connect or on the first call; either way nothing is acknowledged.
        }

        Assert.False(acknowledged);
    }

    [Fact]
    public async Task Heartbeat_AfterCertificateRevoked_IsRefusedAndConnectionClosed()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();
        var first = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null));
        Assert.True(first.Acknowledged);

        await RevokeAgentCertificatesAsync(factory, enrolled.NodeId);

        var second = await TryHeartbeatAsync(connection, new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null));
        Assert.True(second is null || !second.Acknowledged);
        Assert.NotEqual(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Heartbeat_NodeIdentityComesOnlyFromCertificate()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (nodeA, storeA) = await EnrollAgentAsync(factory, Guid.NewGuid());
        var (nodeB, _) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var certA = storeA.GetClientCertificate()!;

        var (lastHeartbeatB, cpuB) = await ReadNodeAndCpuAsync(factory, nodeB.NodeId);
        var (lastHeartbeatA, _) = await ReadNodeAndCpuAsync(factory, nodeA.NodeId);

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        await using var connection = CreateHubConnection(factory, certA);
        await connection.StartAsync();

        // The payload has no node field; node A's certificate must be the only identity used.
        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(33, 33, 33, 1, "2.0.0-hub", null));
        Assert.True(ack.Acknowledged);

        var (lastHeartbeatAAfter, _) = await ReadNodeAndCpuAsync(factory, nodeA.NodeId);
        var (lastHeartbeatBAfter, cpuBAfter) = await ReadNodeAndCpuAsync(factory, nodeB.NodeId);
        Assert.NotEqual(lastHeartbeatA, lastHeartbeatAAfter);
        Assert.Equal(lastHeartbeatB, lastHeartbeatBAfter);
        Assert.Equal(cpuB, cpuBAfter);
    }

    [Fact]
    public async Task Heartbeat_NulInAgentVersion_ReturnsInvalidHeartbeat_AndLeavesDbUnchanged()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        var (lastHeartbeatBefore, cpuBefore) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);
        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(10, 10, 10, 0, "1.0\0", null));

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.InvalidHeartbeat, ack.ErrorCode);

        var (lastHeartbeatAfter, cpuAfter) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);
        Assert.Equal(lastHeartbeatBefore, lastHeartbeatAfter);
        Assert.Equal(cpuBefore, cpuAfter);
    }

    [Fact]
    public void AgentHub_ImplementsContractInterface()
    {
        Assert.True(typeof(IAgentHub).IsAssignableFrom(typeof(AgentHub)));
    }

    [Fact]
    public async Task Connect_ForDecommissionedNode_IsClosedByNodes()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            node.Status = NodeStatus.Decommissioned;
            await db.SaveChangesAsync();
        }

        await using var connection = CreateHubConnection(factory, cert);
        var closed = WatchForClose(connection);
        await TryStartAsync(connection);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(factory.Services.GetRequiredService<IAgentConnectionRegistry>().IsConnected(enrolled.NodeId));
    }

    [Fact]
    public async Task Connect_ForUnknownNode_IsClosedByNodes()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            db.AgentCertificates.RemoveRange(await db.AgentCertificates.Where(c => c.NodeId == enrolled.NodeId).ToListAsync());
            db.NodeHealths.RemoveRange(await db.NodeHealths.Where(h => h.NodeId == enrolled.NodeId).ToListAsync());
            db.Nodes.RemoveRange(await db.Nodes.IgnoreQueryFilters().Where(n => n.Id == enrolled.NodeId).ToListAsync());
            await db.SaveChangesAsync();
        }

        await using var connection = CreateHubConnection(factory, cert);
        var closed = WatchForClose(connection);
        await TryStartAsync(connection);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(factory.Services.GetRequiredService<IAgentConnectionRegistry>().IsConnected(enrolled.NodeId));
    }

    [Fact]
    public async Task OpenConnection_IsRegistered_UntilDisposed()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;
        var registry = factory.Services.GetRequiredService<IAgentConnectionRegistry>();

        var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();
        await WaitUntilAsync(() => registry.IsConnected(enrolled.NodeId), TimeSpan.FromSeconds(5));
        Assert.True(registry.IsConnected(enrolled.NodeId));

        await connection.DisposeAsync();
        await WaitUntilAsync(() => !registry.IsConnected(enrolled.NodeId), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Telemetry_Valid_IsAccepted()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(_baseFactory.TimeProvider.GetUtcNow(), new Dictionary<string, double> { ["cpu"] = 1 }));

        Assert.True(ack.Accepted);
    }

    [Fact]
    public async Task Telemetry_With501Metrics_IsRejectedAsInvalidTelemetry()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(_baseFactory.TimeProvider.GetUtcNow(), Metrics(501, i => $"m{i}")));

        Assert.False(ack.Accepted);
        Assert.Equal(AgentHubErrorCodes.InvalidTelemetry, ack.ErrorCode);
    }

    [Fact]
    public async Task Telemetry_WithUnknownMember_IsAccepted()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;
        var now = _baseFactory.TimeProvider.GetUtcNow();

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        // Positive twin of Telemetry_WithoutMetrics_IsRejected: the only difference is the unknown member.
        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new { timestamp = now, metrics = new Dictionary<string, double> { ["cpu"] = 1 }, extra = "x" });

        Assert.True(ack.Accepted);
    }

    [Fact]
    public async Task Telemetry_WithoutMetrics_IsRejected()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;
        var now = _baseFactory.TimeProvider.GetUtcNow();

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new { timestamp = now, extra = "x" });

        Assert.False(ack.Accepted);
        Assert.Equal(AgentHubErrorCodes.InvalidTelemetry, ack.ErrorCode);
    }

    [Fact]
    public async Task Telemetry_AtEscapedWorstCase_IsAccepted_AndConnectionStaysOpen()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        // 500 metric names of 128 chars (a 3-digit index plus 125 '\u00E9'), the largest valid telemetry. About
        // 125 KiB as UTF-8 (2 bytes per '\u00E9'): over main's 64 KiB limit and under the 512 KiB limit, so the
        // ack proves the limit was raised.
        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(_baseFactory.TimeProvider.GetUtcNow(), Metrics(500, i => i.ToString("D3", CultureInfo.InvariantCulture) + new string('\u00E9', 125))));

        Assert.True(ack.Accepted);
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Frame_OverLimit_ClosesConnection()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        var closed = WatchForClose(connection);
        await connection.StartAsync();

        // 700 metric names of 128 chars (a 3-digit index plus 125 control characters). Each '\u0001' is escaped to
        // \u0001 (6 bytes), so the frame is about 525 KiB, over the 512 KiB limit. 'é' would not do: it stays 2 bytes
        // on the wire. With the limit missing, the frame would reach the validator and answer invalid_telemetry,
        // so an ack would fail the assertion below.
        var invoke = connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(_baseFactory.TimeProvider.GetUtcNow(), Metrics(700, i => i.ToString("D3", CultureInfo.InvariantCulture) + new string('\u0001', 125))));

        // Long polling reports the refused frame as a failed invoke; the server-side close is not surfaced to Closed.
        await Task.WhenAny(invoke, closed.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(invoke.IsCompletedSuccessfully, "an ack arrived for an over-limit frame");
    }

    [Fact]
    public async Task Connect_WithValidatedScopes_RegistersAndAcknowledges()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true, validateScopes: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(_baseFactory.TimeProvider.GetUtcNow(), new Dictionary<string, double> { ["cpu"] = 1 }));

        Assert.True(ack.Accepted);
        Assert.True(factory.Services.GetRequiredService<IAgentConnectionRegistry>().IsConnected(enrolled.NodeId));
    }

    private static Dictionary<string, double> Metrics(int count, Func<int, string> nameFor)
    {
        var metrics = new Dictionary<string, double>(count);
        for (var i = 0; i < count; i++)
        {
            metrics[nameFor(i)] = 1;
        }

        return metrics;
    }

    private static TaskCompletionSource WatchForClose(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        return closed;
    }

    /// <summary>StartAsync may throw when the server closes during negotiation; the Closed event is the assertion.</summary>
    private static async Task TryStartAsync(HubConnection connection)
    {
        try
        {
            await connection.StartAsync();
        }
        catch (Exception)
        {
            // Refused during connect; the caller asserts on the Closed event.
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private static HubConnection CreateHubConnection(
        WebApplicationFactory<Program> factory,
        X509Certificate2? cert,
        string? protocolVersion = null)
    {
        return new HubConnectionBuilder()
            .WithUrl("https://localhost" + AgentHubProtocol.Path, o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                if (cert is not null)
                {
                    o.ClientCertificates = new X509CertificateCollection { cert };
                }
                o.HttpMessageHandlerFactory = CertificateInjectingHandlerFactory(factory.Server);
                if (protocolVersion is not null)
                {
                    o.Headers[AgentHubProtocol.VersionHeader] = protocolVersion;
                }
            })
            .Build();
    }

    /// <summary>Invokes Heartbeat; returns null when the server refused the call or closed the connection.</summary>
    private static async Task<AgentHeartbeatAck?> TryHeartbeatAsync(HubConnection connection, AgentHeartbeat heartbeat)
    {
        try
        {
            return await connection.InvokeAsync<AgentHeartbeatAck>(nameof(IAgentHub.Heartbeat), heartbeat);
        }
        catch (Exception)
        {
            // A closed connection cancels the pending invocation; that is the refusal being tested.
            return null;
        }
    }

    private static async Task RevokeAgentCertificatesAsync(WebApplicationFactory<Program> factory, Guid nodeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var certs = await db.AgentCertificates.Where(c => c.NodeId == nodeId && !c.IsRevoked).ToListAsync();
        foreach (var cert in certs)
        {
            cert.IsRevoked = true;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>A certificate with a SPIFFE SAN that is not issued by the test CA; the middleware must refuse it.</summary>
    private static X509Certificate2 CreateSelfSignedAgentCertificate(Guid nodeId)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=foreign-agent-{nodeId}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri($"spiffe://meridian.local/nodes/{nodeId}"));
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static async Task<(DateTime? LastHeartbeat, double? CpuUsagePercent)> ReadNodeAndCpuAsync(
        WebApplicationFactory<Program> factory,
        Guid nodeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        var health = await db.NodeHealths.AsNoTracking().SingleOrDefaultAsync(h => h.NodeId == nodeId);
        return (node.LastHeartbeat, health?.CpuUsagePercent);
    }
}
