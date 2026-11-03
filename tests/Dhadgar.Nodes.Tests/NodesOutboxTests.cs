using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Messaging;
using Dhadgar.Nodes.Services;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.DependencyInjection;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Proves that Nodes publishes go through the EF bus outbox: a publish made before a failed SaveChanges reaches
/// nothing, and a committed reservation is delivered exactly once, including after a transient retry.
/// </summary>
public sealed class NodesOutboxTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task ReserveAsync_SaveChangesFails_PublishesNothing()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var failSave = new FailSaveInterceptor();
        await using var provider = BuildProvider(connection, failSave);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var nodeId = await SeedNodeAsync(provider, timeProvider);

        // Act
        failSave.Armed = true;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15));
        }

        // Assert - nothing stored and nothing delivered
        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var verify = verifyScope.ServiceProvider.GetRequiredService<NodesDbContext>();
            Assert.Equal(0, await verify.Set<OutboxMessage>().CountAsync());
        }

        Assert.False(await harness.Published.Any<CapacityReserved>());
    }

    [Fact]
    public async Task ReserveAsync_Commits_DeliversExactlyOne()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var provider = BuildProvider(connection);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var nodeId = await SeedNodeAsync(provider, timeProvider);

        // Act
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            var result = await service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15);
            Assert.True(result.Success);
        }

        // Assert
        Assert.True(await harness.Published.Any<CapacityReserved>());
        Assert.Single(harness.Published.Select<CapacityReserved>());
    }

    [Fact]
    public async Task ReserveAsync_TransientFailureOnInsert_DeliversExactlyOne()
    {
        // Arrange - the first reservation INSERT fails transiently; the retry must publish one event for one row
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var interceptor = new CapacityReservationServiceTests.FailFirstInsertInterceptor();
        await using var provider = BuildProvider(
            connection,
            interceptor,
            sqlite => sqlite.ExecutionStrategy(d => new CapacityReservationServiceTests.TestRetryingStrategy(d)));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var nodeId = await SeedNodeAsync(provider, timeProvider);

        // Act
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            var result = await service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15);
            Assert.True(result.Success);
        }

        // Assert
        Assert.Equal(1, interceptor.Throws);
        Assert.True(await harness.Published.Any<CapacityReserved>());
        Assert.Single(harness.Published.Select<CapacityReserved>());

        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<NodesDbContext>();
        Assert.Single(await verify.CapacityReservations.ToListAsync());
    }

    [Fact]
    public async Task Outbox_Wiring_UsesTheEfScopedBusContextProvider()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var provider = BuildProvider(connection);

        // Act
        await using var scope = provider.CreateAsyncScope();
        var contextProvider = scope.ServiceProvider.GetRequiredService<IScopedBusContextProvider<IBus>>();

        // Assert
        Assert.StartsWith("EntityFrameworkScopedBusContextProvider", contextProvider.GetType().Name, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection,
        IInterceptor? interceptor = null,
        Action<SqliteDbContextOptionsBuilder>? sqlite = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        services.AddDbContext<NodesDbContext>((_, o) =>
        {
            o.UseSqlite(connection, s => sqlite?.Invoke(s));
            if (interceptor is not null)
            {
                o.AddInterceptors(interceptor);
            }
        });
        services.AddSingleton<ILogger<CapacityReservationService>>(NullLogger<CapacityReservationService>.Instance);
        services.AddScoped<ICapacityReservationService, CapacityReservationService>();
        services.AddMassTransitTestHarness(x => NodesMessaging.AddNodesOutbox(x, o => o.UseSqlite()));

        var provider = services.BuildServiceProvider(validateScopes: true);
        using (var schemaScope = provider.CreateScope())
        {
            schemaScope.ServiceProvider.GetRequiredService<NodesDbContext>().Database.EnsureCreated();
        }

        return provider;
    }

    /// <summary>Seeds one online node with capacity, without publishing anything.</summary>
    private static async Task<Guid> SeedNodeAsync(IServiceProvider provider, TimeProvider timeProvider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            Name = "outbox-node",
            Status = NodeStatus.Online,
            Platform = "linux",
            CreatedAt = utcNow
        };
        context.Nodes.Add(node);
        context.HardwareInventories.Add(new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Hostname = "test-host",
            CpuCores = 8,
            MemoryBytes = 32L * 1024 * 1024 * 1024,
            DiskBytes = 1000L * 1024 * 1024 * 1024,
            CollectedAt = utcNow
        });
        context.NodeCapacities.Add(new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = 10,
            CurrentGameServers = 2,
            AvailableMemoryBytes = 16L * 1024 * 1024 * 1024,
            AvailableDiskBytes = 500L * 1024 * 1024 * 1024,
            UpdatedAt = utcNow
        });
        await context.SaveChangesAsync();
        return node.Id;
    }

    /// <summary>Fails every save with a non-transient <see cref="DbUpdateException"/> while <see cref="Armed"/> is set.</summary>
    private sealed class FailSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                throw new DbUpdateException("Simulated save failure", (Exception?)null);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
