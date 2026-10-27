using System.Globalization;
using System.Security.Claims;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Hubs;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Hubs;

/// <summary>
/// Unit tests for the certificate-expiry branch of <see cref="AgentHub"/>. These use their own clock, so the
/// certificate can be placed before or after its NotAfter time without touching the shared hub test factory.
/// </summary>
public sealed class AgentHubCertificateExpiryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly AgentHeartbeat ValidHeartbeat = new(10, 10, 10, 0, "2.0.0-hub", null);

    [Fact]
    public async Task Heartbeat_CertificatePastNotAfter_IsRefusedAndAborted()
    {
        var time = new FakeTimeProvider(Now);
        using var db = CreateContext();
        var heartbeats = CreateHeartbeatService();
        var context = CreateHubContext(Guid.NewGuid(), Now.AddSeconds(-1), includeNotAfter: true);
        using var hub = CreateHub(db, heartbeats, allowExpired: false, time, context);

        var ack = await hub.Heartbeat(ValidHeartbeat);

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.HeartbeatFailed, ack.ErrorCode);
        context.Received(1).Abort();
        await heartbeats.DidNotReceive().ProcessHeartbeatAsync(
            Arg.Any<Guid>(), Arg.Any<HeartbeatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Heartbeat_CertificateBeforeNotAfter_IsAcknowledged()
    {
        var time = new FakeTimeProvider(Now);
        using var db = CreateContext();
        var heartbeats = CreateHeartbeatService();
        var context = CreateHubContext(Guid.NewGuid(), Now.AddDays(1), includeNotAfter: true);
        using var hub = CreateHub(db, heartbeats, allowExpired: false, time, context);

        var ack = await hub.Heartbeat(ValidHeartbeat);

        Assert.True(ack.Acknowledged);
        context.DidNotReceive().Abort();
    }

    [Fact]
    public async Task Heartbeat_ExpiredCertificate_WithAllowExpired_IsAcknowledged()
    {
        var time = new FakeTimeProvider(Now);
        using var db = CreateContext();
        var heartbeats = CreateHeartbeatService();
        var context = CreateHubContext(Guid.NewGuid(), Now.AddSeconds(-1), includeNotAfter: true);
        using var hub = CreateHub(db, heartbeats, allowExpired: true, time, context);

        var ack = await hub.Heartbeat(ValidHeartbeat);

        Assert.True(ack.Acknowledged);
    }

    [Fact]
    public async Task Heartbeat_MissingNotAfterClaim_IsRefused()
    {
        var time = new FakeTimeProvider(Now);
        using var db = CreateContext();
        var heartbeats = CreateHeartbeatService();
        var context = CreateHubContext(Guid.NewGuid(), Now.AddDays(1), includeNotAfter: false);
        using var hub = CreateHub(db, heartbeats, allowExpired: false, time, context);

        var ack = await hub.Heartbeat(ValidHeartbeat);

        Assert.False(ack.Acknowledged);
        context.Received(1).Abort();
    }

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static IHeartbeatService CreateHeartbeatService()
    {
        var heartbeats = Substitute.For<IHeartbeatService>();
        heartbeats
            .ProcessHeartbeatAsync(Arg.Any<Guid>(), Arg.Any<HeartbeatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ServiceResult.Ok(true)));
        return heartbeats;
    }

    private static HubCallerContext CreateHubContext(Guid nodeId, DateTimeOffset notAfter, bool includeNotAfter)
    {
        var claims = new List<Claim>
        {
            new(AgentHubProtocol.NodeIdClaim, nodeId.ToString()),
            new("certificate_thumbprint", "abc"),
        };

        if (includeNotAfter)
        {
            claims.Add(new Claim(
                "certificate_not_after",
                notAfter.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var context = Substitute.For<HubCallerContext>();
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "mTLS")));
        context.ConnectionAborted.Returns(CancellationToken.None);
        return context;
    }

    private static AgentHub CreateHub(
        NodesDbContext db,
        IHeartbeatService heartbeats,
        bool allowExpired,
        TimeProvider time,
        HubCallerContext context)
    {
        var options = Options.Create(new MtlsOptions { AllowExpiredCertificates = allowExpired });
        return new AgentHub(heartbeats, db, options, time, NullLogger<AgentHub>.Instance)
        {
            Context = context,
        };
    }
}
