using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

// Alias local models to avoid ambiguity with Contracts types
using EnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using HardwareInventoryDto = Dhadgar.Nodes.Models.HardwareInventoryDto;

namespace Dhadgar.Nodes.Tests;

public sealed class EnrollmentServiceTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();
    private const string TestUserId = "user-123";

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions() =>
        Options.Create(new NodesOptions());

    private static EnrollmentTokenService CreateTokenService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        return new EnrollmentTokenService(
            context,
            new TestAuditService(),
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            CreateOptions(),
            NullLogger<EnrollmentTokenService>.Instance);
    }

    private static async Task<(EnrollmentService Service, TestNodesEventPublisher Publisher, TestCertificateAuthorityService CaService)> CreateServiceAsync(
        NodesDbContext context,
        IEnrollmentTokenService tokenService,
        FakeTimeProvider? timeProvider = null)
    {
        var provider = timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publisher = new TestNodesEventPublisher();
        var caService = new TestCertificateAuthorityService(provider);
        var auditService = new TestAuditService();
        await caService.InitializeAsync();

        var service = new EnrollmentService(
            context,
            tokenService,
            caService,
            auditService,
            publisher,
            provider,
            CreateOptions(),
            NullLogger<EnrollmentService>.Instance);
        return (service, publisher, caService);
    }

    private static HardwareInventoryDto CreateHardware(
        string hostname = "test-server",
        int cpuCores = 8,
        long memoryBytes = 16L * 1024 * 1024 * 1024,
        long diskBytes = 500L * 1024 * 1024 * 1024) => new(
            Hostname: hostname,
            OsVersion: "Ubuntu 22.04",
            CpuCores: cpuCores,
            MemoryBytes: memoryBytes,
            DiskBytes: diskBytes,
            NetworkInterfaces: null);

    [Fact]
    public async Task EnrollAsync_ValidToken_CreatesNode()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware("my-server"));

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.Value!.NodeId);

        var node = await context.Nodes.FindAsync(result.Value.NodeId);
        Assert.NotNull(node);
        Assert.Equal(TestOrgId, node.OrganizationId);
        Assert.Equal("linux", node.Platform);
        Assert.Equal(NodeStatus.Enrolling, node.Status);
    }

    [Fact]
    public async Task EnrollAsync_CreatesHardwareInventory()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "server-01",
                OsVersion: "Debian 12",
                CpuCores: 16,
                MemoryBytes: 32L * 1024 * 1024 * 1024,
                DiskBytes: 1000L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var hardware = await context.HardwareInventories
            .FirstOrDefaultAsync(h => h.NodeId == result.Value!.NodeId);
        Assert.NotNull(hardware);
        Assert.Equal("server-01", hardware.Hostname);
        Assert.Equal("Debian 12", hardware.OsVersion);
        Assert.Equal(16, hardware.CpuCores);
        Assert.Equal(32L * 1024 * 1024 * 1024, hardware.MemoryBytes);
    }

    [Fact]
    public async Task EnrollAsync_CreatesCapacityRecord()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        // 32GB RAM, 8 cores = min(32/4, 8/2) = min(8, 4) = 4 max servers
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "server-01",
                OsVersion: "Ubuntu",
                CpuCores: 8,
                MemoryBytes: 32L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var capacity = await context.NodeCapacities
            .FirstOrDefaultAsync(c => c.NodeId == result.Value!.NodeId);
        Assert.NotNull(capacity);
        Assert.Equal(4, capacity.MaxGameServers); // Based on heuristic
        Assert.Equal(0, capacity.CurrentGameServers);
        Assert.Equal(32L * 1024 * 1024 * 1024, capacity.AvailableMemoryBytes);
    }

    [Fact]
    public async Task EnrollAsync_CreatesAgentCertificate()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var cert = await context.AgentCertificates
            .FirstOrDefaultAsync(c => c.NodeId == result.Value!.NodeId);
        Assert.NotNull(cert);
        Assert.Equal(64, cert.Thumbprint.Length); // 32 bytes = 64 hex chars
        Assert.False(cert.IsRevoked);
        Assert.Equal(result.Value!.CertificateThumbprint, cert.Thumbprint);
    }

    [Fact]
    public async Task EnrollAsync_ReturnsCertificateInResponse()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.NotEmpty(result.Value!.CertificateThumbprint);
        Assert.Contains("BEGIN CERTIFICATE", result.Value.Certificate, StringComparison.Ordinal);
        Assert.Contains("END CERTIFICATE", result.Value.Certificate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnrollAsync_MarksTokenAsUsed()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (token, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var updatedToken = await context.EnrollmentTokens.FindAsync(token.Id);
        Assert.NotNull(updatedToken!.UsedAt);
        Assert.Equal(result.Value!.NodeId, updatedToken.UsedByNodeId);
    }

    [Fact]
    public async Task EnrollAsync_PublishesNodeEnrolledEvent()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, publisher, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.True(publisher.HasMessage<NodeEnrolled>());
        var evt = publisher.GetLastMessage<NodeEnrolled>()!;
        Assert.Equal(result.Value!.NodeId, evt.NodeId);
        Assert.Equal(TestOrgId, evt.OrganizationId);
        Assert.Equal("linux", evt.Platform);
    }

    [Fact]
    public async Task EnrollAsync_InvalidToken_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var request = new EnrollNodeRequest(
            Token: "invalid-token-that-does-not-exist",
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("invalid_token", result.Error);
    }

    [Fact]
    public async Task EnrollAsync_ExpiredToken_ReturnsFail()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var tokenService = CreateTokenService(context, timeProvider);
        var (service, _, _) = await CreateServiceAsync(context, tokenService, timeProvider);

        var (_, plainToken) = await tokenService.CreateTokenAsync(
            TestOrgId, TestUserId, "Test", TimeSpan.FromMinutes(30));

        // Advance past expiration
        timeProvider.Advance(TimeSpan.FromHours(1));

        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("invalid_token", result.Error);
    }

    [Fact]
    public async Task EnrollAsync_AlreadyUsedToken_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // First enrollment succeeds
        await service.EnrollAsync(request);

        // Act - second enrollment fails
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("invalid_token", result.Error);
    }

    [Fact]
    public async Task EnrollAsync_InvalidPlatform_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "macos", // Invalid - only linux and windows supported
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("invalid_platform", result.Error);
    }

    [Fact]
    public async Task EnrollAsync_WindowsPlatform_Succeeds()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "Windows", // Mixed case should work
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        Assert.True(result.Success);
        var node = await context.Nodes.FindAsync(result.Value!.NodeId);
        Assert.Equal("windows", node!.Platform); // Normalized to lowercase
    }

    [Fact]
    public async Task EnrollAsync_GeneratesUniqueNodeName()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        // First enrollment
        var (_, token1) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test1");
        var result1 = await service.EnrollAsync(new EnrollNodeRequest(
            Token: token1,
            Platform: "linux",
            Hardware: CreateHardware("server-01")));

        // Second enrollment with same hostname
        var (_, token2) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test2");
        var result2 = await service.EnrollAsync(new EnrollNodeRequest(
            Token: token2,
            Platform: "linux",
            Hardware: CreateHardware("server-01")));

        // Third enrollment with same hostname
        var (_, token3) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test3");
        var result3 = await service.EnrollAsync(new EnrollNodeRequest(
            Token: token3,
            Platform: "linux",
            Hardware: CreateHardware("server-01")));

        // Assert
        var node1 = await context.Nodes.FindAsync(result1.Value!.NodeId);
        var node2 = await context.Nodes.FindAsync(result2.Value!.NodeId);
        var node3 = await context.Nodes.FindAsync(result3.Value!.NodeId);

        Assert.Equal("server-01", node1!.Name);
        Assert.Equal("server-01-2", node2!.Name);
        Assert.Equal("server-01-3", node3!.Name);
    }

    [Fact]
    public async Task EnrollAsync_SanitizesHostname()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware("My Server!@#$%^&*()"));

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var node = await context.Nodes.FindAsync(result.Value!.NodeId);
        // Should only contain lowercase letters, digits, and hyphens
        Assert.Matches("^[a-z0-9-]+$", node!.Name);
    }

    [Fact]
    public async Task EnrollAsync_SetsDisplayNameFromHostname()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware("Production-Server-01"));

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var node = await context.Nodes.FindAsync(result.Value!.NodeId);
        Assert.Equal("Production-Server-01", node!.DisplayName);
    }

    [Fact]
    public async Task EnrollAsync_CertificateValidity90Days()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var tokenService = CreateTokenService(context, timeProvider);
        var (service, _, _) = await CreateServiceAsync(context, tokenService, timeProvider);

        var (_, plainToken) = await tokenService.CreateTokenAsync(TestOrgId, TestUserId, "Test");
        var request = new EnrollNodeRequest(
            Token: plainToken,
            Platform: "linux",
            Hardware: CreateHardware());

        // Act
        var result = await service.EnrollAsync(request);

        // Assert
        var cert = await context.AgentCertificates
            .FirstOrDefaultAsync(c => c.NodeId == result.Value!.NodeId);
        Assert.NotNull(cert);
        Assert.Equal(now.UtcDateTime, cert.NotBefore);
        Assert.Equal(now.UtcDateTime.AddDays(90), cert.NotAfter);
    }

    [Fact]
    public async Task EnrollAsync_DifferentOrganizations_SameHostnameAllowed()
    {
        // Arrange
        using var context = CreateContext();
        var tokenService = CreateTokenService(context);
        var (service, _, _) = await CreateServiceAsync(context, tokenService);

        var org1 = Guid.NewGuid();
        var org2 = Guid.NewGuid();

        var (_, token1) = await tokenService.CreateTokenAsync(org1, TestUserId, "Org1");
        var (_, token2) = await tokenService.CreateTokenAsync(org2, TestUserId, "Org2");

        // Act
        var result1 = await service.EnrollAsync(new EnrollNodeRequest(
            Token: token1,
            Platform: "linux",
            Hardware: CreateHardware("shared-name")));

        var result2 = await service.EnrollAsync(new EnrollNodeRequest(
            Token: token2,
            Platform: "linux",
            Hardware: CreateHardware("shared-name")));

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);

        var node1 = await context.Nodes.FindAsync(result1.Value!.NodeId);
        var node2 = await context.Nodes.FindAsync(result2.Value!.NodeId);

        // Same name but different orgs
        Assert.Equal("shared-name", node1!.Name);
        Assert.Equal("shared-name", node2!.Name);
        Assert.NotEqual(node1.OrganizationId, node2.OrganizationId);
    }
}
