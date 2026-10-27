using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using EnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using HardwareInventoryDto = Dhadgar.Nodes.Models.HardwareInventoryDto;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// D-S246 (campaign-security-1 N1): two enrollments that validated the same one-time token must not both win.
/// Deterministic: each request stops inside certificate issuance (after ValidateTokenAsync, before its save)
/// until the test releases it, so B always validates before A saves. SQLite runs the real relational UPDATE.
/// </summary>
public sealed class EnrollmentTokenRaceTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task EnrollAsync_SameTokenValidatedByTwoRequests_OnlyTheFirstToSaveEnrolls()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NodesDbContext>().UseSqlite(connection).Options;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var orgId = Guid.NewGuid();

        Guid tokenId;
        string plainToken;
        using (var setup = new NodesDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var (token, plain) = await CreateTokenService(setup, time).CreateTokenAsync(orgId, "admin-1", "race");
            tokenId = token.Id;
            plainToken = plain;
        }

        var ca = new TestCertificateAuthorityService(time);
        await ca.InitializeAsync();
        var gateA = new GatedCertificateAuthority(ca);
        var gateB = new GatedCertificateAuthority(ca);

        using var contextA = new NodesDbContext(options);
        using var contextB = new NodesDbContext(options);
        var serviceA = CreateEnrollmentService(contextA, gateA, time);
        var serviceB = CreateEnrollmentService(contextB, gateB, time);

        // Act
        var taskA = serviceA.EnrollAsync(Request(plainToken, "host-a"));
        await gateA.Entered.Task.WaitAsync(StepTimeout);   // A validated the token and holds before its save
        var taskB = serviceB.EnrollAsync(Request(plainToken, "host-b"));
        await gateB.Entered.Task.WaitAsync(StepTimeout);   // B validated the same, still unused, token

        gateA.Release.SetResult();
        var resultA = await taskA.WaitAsync(StepTimeout);
        gateB.Release.SetResult();
        var resultB = await taskB.WaitAsync(StepTimeout);

        // Assert
        Assert.True(resultA.Success);
        Assert.False(resultB.Success);
        Assert.Equal("invalid_token", resultB.Error);

        using var verify = new NodesDbContext(options);
        var node = Assert.Single(await verify.Nodes.AsNoTracking().ToListAsync());
        Assert.Equal(resultA.Value!.NodeId, node.Id);
        Assert.Equal(node.Id, Assert.Single(await verify.AgentCertificates.AsNoTracking().ToListAsync()).NodeId);
        Assert.Single(await verify.HardwareInventories.AsNoTracking().ToListAsync());
        Assert.Single(await verify.NodeCapacities.AsNoTracking().ToListAsync());

        var storedToken = await verify.EnrollmentTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        Assert.Equal(node.Id, storedToken.UsedByNodeId);

        // The loser's failure is audited (proves the change tracker was cleared before the audit save).
        Assert.Equal(1, await verify.AuditLogs.AsNoTracking().CountAsync(a =>
            a.Action == AuditActions.EnrollmentFailed && a.FailureReason == "invalid_token"));
    }

    private static EnrollNodeRequest Request(string token, string hostname) => new(
        Token: token,
        Platform: "linux",
        PublicKey: TestAgentKeys.PublicKeyBase64,
        Hardware: new HardwareInventoryDto(
            Hostname: hostname,
            OsVersion: "Ubuntu 22.04",
            CpuCores: 8,
            MemoryBytes: 16L * 1024 * 1024 * 1024,
            DiskBytes: 500L * 1024 * 1024 * 1024,
            NetworkInterfaces: null));

    private static EnrollmentTokenService CreateTokenService(NodesDbContext context, TimeProvider time) =>
        new(context, CreateAuditService(context, time), time, Options.Create(new NodesOptions()),
            NullLogger<EnrollmentTokenService>.Instance);

    private static EnrollmentService CreateEnrollmentService(
        NodesDbContext context, ICertificateAuthorityService ca, TimeProvider time) =>
        new(context,
            CreateTokenService(context, time),
            ca,
            CreateAuditService(context, time),
            new TestNodesEventPublisher(),
            time,
            Options.Create(new NodesOptions()),
            NullLogger<EnrollmentService>.Instance);

    // The real AuditService: it saves through the same DbContext as the enrollment, which is what D-S246's
    // ChangeTracker.Clear() protects.
    private static AuditService CreateAuditService(NodesDbContext context, TimeProvider time)
    {
        var accessor = Substitute.For<IAuditContextAccessor>();
        accessor.GetActorId().Returns("race-test-agent");
        accessor.GetActorType().Returns(ActorType.Agent);
        return new AuditService(context, accessor, time, NullLogger<AuditService>.Instance);
    }

    private sealed class GatedCertificateAuthority(ICertificateAuthorityService inner) : ICertificateAuthorityService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken ct = default) => inner.InitializeAsync(ct);

        public Task<CertificateIssuanceResult> IssueCertificateAsync(Guid nodeId, CancellationToken ct = default) =>
            inner.IssueCertificateAsync(nodeId, ct);

        public async Task<CertificateIssuanceResult> IssueCertificateForPublicKeyAsync(
            Guid nodeId, string subjectPublicKeyInfoBase64, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(StepTimeout, ct);
            return await inner.IssueCertificateForPublicKeyAsync(nodeId, subjectPublicKeyInfoBase64, ct);
        }

        public Task<string> GetCaCertificatePemAsync(CancellationToken ct = default) =>
            inner.GetCaCertificatePemAsync(ct);

        public Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default) =>
            inner.ValidateCertificateAsync(certificatePem, ct);
    }
}
