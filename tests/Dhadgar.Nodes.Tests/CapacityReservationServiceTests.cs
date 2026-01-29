using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class CapacityReservationServiceTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static (CapacityReservationService Service, TestNodesEventPublisher Publisher, FakeTimeProvider TimeProvider) CreateService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        var publisher = new TestNodesEventPublisher();
        var tp = timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new CapacityReservationService(
            context,
            publisher,
            tp,
            NullLogger<CapacityReservationService>.Instance);
        return (service, publisher, tp);
    }

    private static async Task<Node> SeedNodeWithCapacityAsync(
        NodesDbContext context,
        TimeProvider timeProvider,
        Guid? orgId = null,
        NodeStatus status = NodeStatus.Online,
        long availableMemoryBytes = 16L * 1024 * 1024 * 1024, // 16GB
        long availableDiskBytes = 500L * 1024 * 1024 * 1024) // 500GB
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId ?? TestOrgId,
            Name = $"test-node-{Guid.NewGuid():N}".Substring(0, 20),
            Status = status,
            Platform = "linux",
            CreatedAt = utcNow
        };

        var hardware = new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Hostname = "test-host",
            CpuCores = 8,
            MemoryBytes = 32L * 1024 * 1024 * 1024, // 32GB
            DiskBytes = 1000L * 1024 * 1024 * 1024, // 1TB
            CollectedAt = utcNow
        };

        var capacity = new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = 10,
            CurrentGameServers = 2,
            AvailableMemoryBytes = availableMemoryBytes,
            AvailableDiskBytes = availableDiskBytes,
            UpdatedAt = utcNow
        };

        context.Nodes.Add(node);
        context.HardwareInventories.Add(hardware);
        context.NodeCapacities.Add(capacity);
        await context.SaveChangesAsync();

        return node;
    }

    [Fact]
    public async Task ReserveAsync_ValidRequest_CreatesReservation()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 10240,
            cpuMillicores: 1000,
            requestedBy: "tasks-service",
            ttlMinutes: 15);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(node.Id, result.Value.NodeId);
        Assert.Equal(1024, result.Value.MemoryMb);
        Assert.Equal(10240, result.Value.DiskMb);
        Assert.Equal(1000, result.Value.CpuMillicores);
        Assert.Equal("tasks-service", result.Value.RequestedBy);
        Assert.Equal(ReservationStatus.Pending, result.Value.Status);
        Assert.NotEqual(Guid.Empty, result.Value.ReservationToken);
    }

    [Fact]
    public async Task ReserveAsync_ValidRequest_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 10240,
            cpuMillicores: 0,
            requestedBy: "tasks-service",
            ttlMinutes: 15);

        // Assert - verify reservation succeeded before checking event
        Assert.True(result.Success, $"Reservation failed: {result.Error}");
        Assert.NotNull(result.Value);

        Assert.True(publisher.HasMessage<CapacityReserved>());
        var evt = publisher.GetLastMessage<CapacityReserved>()!;
        Assert.Equal(node.Id, evt.NodeId);
        Assert.Equal(1024, evt.MemoryMb);
        Assert.Equal(10240, evt.DiskMb);
        Assert.Equal(result.Value.ReservationToken, evt.ReservationToken);
    }

    [Fact]
    public async Task ReserveAsync_NodeNotFound_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Act
        var result = await service.ReserveAsync(
            Guid.NewGuid(),
            memoryMb: 1024,
            diskMb: 10240,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    [Fact]
    public async Task ReserveAsync_NodeOffline_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider, status: NodeStatus.Offline);

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 10240,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_unavailable", result.Error);
    }

    [Fact]
    public async Task ReserveAsync_NodeInMaintenance_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider, status: NodeStatus.Maintenance);

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 10240,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_unavailable", result.Error);
    }

    [Fact]
    public async Task ReserveAsync_InsufficientMemory_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider,
            availableMemoryBytes: 512L * 1024 * 1024); // Only 512MB available

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024, // Requesting 1GB
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("insufficient_memory", result.Error);
    }

    [Fact]
    public async Task ReserveAsync_InsufficientDisk_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider,
            availableDiskBytes: 5L * 1024 * 1024 * 1024); // Only 5GB available

        // Act
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 10240, // Requesting 10GB
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("insufficient_disk", result.Error);
    }

    [Fact]
    public async Task ReserveAsync_AccountsForExistingReservations()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider,
            availableMemoryBytes: 2L * 1024 * 1024 * 1024); // 2GB available

        // Create existing reservation for 1GB
        await service.ReserveAsync(
            node.Id,
            memoryMb: 1024, // 1GB reserved
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "existing",
            ttlMinutes: 15);

        // Act - try to reserve another 1.5GB
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1536, // 1.5GB - should fail, only ~1GB effective available
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("insufficient_memory", result.Error);
    }

    [Fact]
    public async Task ClaimAsync_ValidReservation_Claims()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        publisher.Clear();

        // Act
        var result = await service.ClaimAsync(reservation.Value!.ReservationToken, "server-123");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(ReservationStatus.Claimed, result.Value!.Status);
        Assert.Equal("server-123", result.Value.ServerId);
        Assert.NotNull(result.Value.ClaimedAt);
    }

    [Fact]
    public async Task ClaimAsync_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        publisher.Clear();

        // Act
        var claimResult = await service.ClaimAsync(reservation.Value!.ReservationToken, "server-123");

        // Assert - verify claim succeeded before checking event
        Assert.True(claimResult.Success, $"Claim failed: {claimResult.Error}");
        Assert.Equal(ReservationStatus.Claimed, claimResult.Value!.Status);

        Assert.True(publisher.HasMessage<CapacityClaimed>());
        var evt = publisher.GetLastMessage<CapacityClaimed>()!;
        Assert.Equal(node.Id, evt.NodeId);
        Assert.Equal(reservation.Value.ReservationToken, evt.ReservationToken);
        Assert.Equal("server-123", evt.ServerId);
    }

    [Fact]
    public async Task ClaimAsync_ExpiredReservation_ReturnsFail()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _, _) = CreateService(context, timeProvider);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 5);

        // Advance time past expiration
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var result = await service.ClaimAsync(reservation.Value!.ReservationToken, "server-123");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("reservation_expired", result.Error);
    }

    [Fact]
    public async Task ClaimAsync_AlreadyClaimed_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        await service.ClaimAsync(reservation.Value!.ReservationToken, "server-123");

        // Act - try to claim again
        var result = await service.ClaimAsync(reservation.Value.ReservationToken, "server-456");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("reservation_claimed", result.Error);
    }

    [Fact]
    public async Task ClaimAsync_NotFound_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Act
        var result = await service.ClaimAsync(Guid.NewGuid(), "server-123");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("reservation_not_found", result.Error);
    }

    [Fact]
    public async Task ReleaseAsync_ValidReservation_Releases()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        publisher.Clear();

        // Act
        var result = await service.ReleaseAsync(reservation.Value!.ReservationToken, "No longer needed");

        // Assert
        Assert.True(result.Success);
        Assert.True(publisher.HasMessage<CapacityReleased>());
    }

    [Fact]
    public async Task ReleaseAsync_AlreadyReleased_IsIdempotent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        var reservation = await service.ReserveAsync(
            node.Id,
            memoryMb: 1024,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        await service.ReleaseAsync(reservation.Value!.ReservationToken);

        // Act - try to release again (should be idempotent)
        var result = await service.ReleaseAsync(reservation.Value.ReservationToken);

        // Assert - idempotent release returns success
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ReleaseAsync_FreesCapacityForNewReservations()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider,
            availableMemoryBytes: 2L * 1024 * 1024 * 1024); // 2GB available

        // Create and then release a reservation
        var reservation1 = await service.ReserveAsync(
            node.Id,
            memoryMb: 1536, // 1.5GB
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        await service.ReleaseAsync(reservation1.Value!.ReservationToken);

        // Act - try to create another 1.5GB reservation (should succeed now)
        var result = await service.ReserveAsync(
            node.Id,
            memoryMb: 1536,
            diskMb: 1024,
            cpuMillicores: 0,
            requestedBy: "test2",
            ttlMinutes: 15);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetAvailableCapacityAsync_ReturnsCorrectValues()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider,
            availableMemoryBytes: 8L * 1024 * 1024 * 1024, // 8GB
            availableDiskBytes: 100L * 1024 * 1024 * 1024); // 100GB

        // Create a reservation
        await service.ReserveAsync(
            node.Id,
            memoryMb: 2048, // 2GB
            diskMb: 20480, // 20GB
            cpuMillicores: 0,
            requestedBy: "test",
            ttlMinutes: 15);

        // Act
        var result = await service.GetAvailableCapacityAsync(node.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(8L * 1024 * 1024 * 1024, result.Value!.AvailableMemoryBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, result.Value.ReservedMemoryBytes);
        Assert.Equal(6L * 1024 * 1024 * 1024, result.Value.EffectiveAvailableMemoryBytes);
        Assert.Equal(1, result.Value.ActiveReservationCount);
    }

    [Fact]
    public async Task GetActiveReservationsAsync_ReturnsOnlyActiveReservations()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, timeProvider) = CreateService(context);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        // Create multiple reservations in different states
        var pending = await service.ReserveAsync(node.Id, 512, 1024, 0, "test1", 15);
        var claimed = await service.ReserveAsync(node.Id, 512, 1024, 0, "test2", 15);
        var released = await service.ReserveAsync(node.Id, 512, 1024, 0, "test3", 15);

        await service.ClaimAsync(claimed.Value!.ReservationToken, "server-1");
        await service.ReleaseAsync(released.Value!.ReservationToken);

        // Act
        var reservations = await service.GetActiveReservationsAsync(node.Id);

        // Assert
        Assert.Equal(2, reservations.Count); // Pending and Claimed only
        Assert.Contains(reservations, r => r.Status == ReservationStatus.Pending);
        Assert.Contains(reservations, r => r.Status == ReservationStatus.Claimed);
        Assert.DoesNotContain(reservations, r => r.Status == ReservationStatus.Released);
    }

    [Fact]
    public async Task ExpireStaleReservationsAsync_ExpiresOldReservations()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context, timeProvider);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        // Create some reservations
        await service.ReserveAsync(node.Id, 512, 1024, 0, "test1", 5); // 5 min TTL
        await service.ReserveAsync(node.Id, 512, 1024, 0, "test2", 5); // 5 min TTL
        await service.ReserveAsync(node.Id, 512, 1024, 0, "test3", 60); // 60 min TTL

        // Advance time past first two TTLs but not third
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        publisher.Clear();

        // Act
        var expiredCount = await service.ExpireStaleReservationsAsync();

        // Assert
        Assert.Equal(2, expiredCount);
        Assert.Equal(2, publisher.GetMessages<CapacityReservationExpired>().Count);
    }

    [Fact]
    public async Task ExpireStaleReservationsAsync_DoesNotExpireClaimedReservations()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context, timeProvider);
        var node = await SeedNodeWithCapacityAsync(context, timeProvider);

        // Create and claim a reservation
        var reservation = await service.ReserveAsync(node.Id, 512, 1024, 0, "test1", 5);
        await service.ClaimAsync(reservation.Value!.ReservationToken, "server-1");

        // Advance time past TTL
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        publisher.Clear();

        // Act
        var expiredCount = await service.ExpireStaleReservationsAsync();

        // Assert
        Assert.Equal(0, expiredCount);
        Assert.False(publisher.HasMessage<CapacityReservationExpired>());
    }

    /// <summary>
    /// Tests that concurrent reservation requests don't over-provision capacity.
    ///
    /// LIMITATION: This test uses SQLite in-memory which does not fully replicate PostgreSQL's
    /// serializable isolation semantics. SQLite's locking is file-based and may not catch all
    /// race conditions that would occur in production with PostgreSQL. For full concurrency
    /// validation, run integration tests against a real PostgreSQL instance in CI.
    /// See: https://www.sqlite.org/isolation.html vs https://www.postgresql.org/docs/current/transaction-iso.html
    /// </summary>
    [Fact]
    public async Task ConcurrentReservations_PreventOverProvisioning()
    {
        // Arrange - use SQLite in-memory with a shared connection for real DB concurrency behavior
        // Note: SQLite serializable != PostgreSQL serializable; see xmldoc for limitations
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseSqlite(connection)
            .Options;

        // Create the schema and seed the node
        var seedTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Guid nodeId;
        using (var setupContext = new NodesDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            var node = await SeedNodeWithCapacityAsync(setupContext, seedTimeProvider,
                availableMemoryBytes: 2L * 1024 * 1024 * 1024); // 2GB
            nodeId = node.Id;
        }

        // Create multiple reservations concurrently, each with its own context and service
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            using var context = new NodesDbContext(options);
            var (service, _, _) = CreateService(context);
            return await service.ReserveAsync(
                nodeId,
                memoryMb: 512, // 0.5GB each = 2.5GB total would exceed
                diskMb: 1024,
                cpuMillicores: 0,
                requestedBy: $"test-{i}",
                ttlMinutes: 15);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        Assert.Equal(4, successCount); // Only 4 should succeed (4 * 512MB = 2GB)
        Assert.Equal(1, failCount); // Last one should fail
        Assert.Equal("insufficient_memory", results.First(r => !r.Success).Error);
    }
}
