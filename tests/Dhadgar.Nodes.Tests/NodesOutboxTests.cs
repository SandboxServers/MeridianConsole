using System.Collections.Concurrent;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Messaging;
using Dhadgar.Nodes.Services;
using MassTransit;
using MassTransit.DependencyInjection;
using MassTransit.EntityFrameworkCoreIntegration;
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
/// Proves that Nodes publishes go through the EF bus outbox: a publish made before a failed SaveChanges reaches nothing,
/// and a committed reservation stores and delivers exactly one event, including after a transient retry (F230).
/// Each context opens its own connection to a shared-cache in-memory database, because the outbox delivery service
/// polls on its own context and SQLite rejects a second transaction on one connection (F231).
/// </summary>
public sealed class NodesOutboxTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task ReserveAsync_SaveChangesFails_PublishesNothing()
    {
        // Arrange
        await using var db = await OutboxDatabase.CreateAsync();
        var failSave = new FailSaveInterceptor();
        var counter = new OutboxWriteCounter();
        await using var provider = BuildProvider(db.ConnectionString, sqlite: null, failSave, counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        var nodeId = await SeedNodeAsync(provider);
        await harness.Start();

        // Act
        failSave.Armed = true;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15));
        }

        // Assert - nothing stored and nothing delivered
        Assert.Equal(0, counter.Saved);
        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var verify = verifyScope.ServiceProvider.GetRequiredService<NodesDbContext>();
            Assert.Equal(0, await verify.Set<OutboxMessage>().CountAsync());
        }

        var probe = harness.GetConsumerHarness<CapacityReservedProbe>();
        Assert.False(await probe.Consumed.Any<CapacityReserved>());
    }

    [Fact]
    public async Task ReserveAsync_Commits_DeliversExactlyOne()
    {
        // Arrange
        await using var db = await OutboxDatabase.CreateAsync();
        var counter = new OutboxWriteCounter();
        await using var provider = BuildProvider(db.ConnectionString, sqlite: null, counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        var nodeId = await SeedNodeAsync(provider);
        await harness.Start();

        // Act
        Guid token;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            var result = await service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15);
            Assert.True(result.Success);
            token = result.Value!.ReservationToken;
        }

        // Assert - one stored event, delivered through the outbox
        Assert.Equal(1, counter.Saved);
        var probe = harness.GetConsumerHarness<CapacityReservedProbe>();
        Assert.True(await probe.Consumed.Any<CapacityReserved>(m => m.Context.Message.ReservationToken == token));
    }

    [Fact]
    public async Task ReserveAsync_TransientFailureOnInsert_DeliversExactlyOne()
    {
        // Arrange - the first reservation INSERT fails transiently; the retry must store one event for one row
        await using var db = await OutboxDatabase.CreateAsync();
        var interceptor = new CapacityReservationServiceTests.FailFirstInsertInterceptor();
        var counter = new OutboxWriteCounter();
        await using var provider = BuildProvider(
            db.ConnectionString,
            sqlite => sqlite.ExecutionStrategy(d => new CapacityReservationServiceTests.TestRetryingStrategy(d)),
            interceptor,
            counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        var nodeId = await SeedNodeAsync(provider);
        await harness.Start();

        // Act
        Guid token;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
            var result = await service.ReserveAsync(
                nodeId, memoryMb: 512, diskMb: 1024, cpuMillicores: 0, requestedBy: "test", ttlMinutes: 15);
            Assert.True(result.Success);
            token = result.Value!.ReservationToken;
        }

        // Assert
        Assert.Equal(1, interceptor.Throws);
        Assert.Equal(1, counter.Saved);
        var probe = harness.GetConsumerHarness<CapacityReservedProbe>();
        Assert.True(await probe.Consumed.Any<CapacityReserved>(m => m.Context.Message.ReservationToken == token));

        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<NodesDbContext>();
        Assert.Single(await verify.CapacityReservations.ToListAsync());
    }

    [Fact]
    public async Task Outbox_Wiring_UsesTheEfScopedBusContextProvider()
    {
        // Arrange
        await using var db = await OutboxDatabase.CreateAsync();
        await using var provider = BuildProvider(db.ConnectionString, sqlite: null);

        // Act
        await using var scope = provider.CreateAsyncScope();
        var contextProvider = scope.ServiceProvider.GetRequiredService<IScopedBusContextProvider<IBus>>();

        // Assert
        Assert.StartsWith("EntityFrameworkScopedBusContextProvider", contextProvider.GetType().Name, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        Action<SqliteDbContextOptionsBuilder>? sqlite,
        params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        services.AddDbContext<NodesDbContext>(o =>
        {
            o.UseSqlite(connectionString, s => sqlite?.Invoke(s));
            o.AddInterceptors(interceptors);
        });
        services.AddSingleton<ILogger<CapacityReservationService>>(NullLogger<CapacityReservationService>.Instance);
        services.AddScoped<ICapacityReservationService, CapacityReservationService>();
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<CapacityReservedProbe>();
            NodesMessaging.AddNodesOutbox(x, o => o.UseSqlite());
        });

        var provider = services.BuildServiceProvider(validateScopes: true);
        using (var schemaScope = provider.CreateScope())
        {
            schemaScope.ServiceProvider.GetRequiredService<NodesDbContext>().Database.EnsureCreated();
        }

        return provider;
    }

    /// <summary>Seeds one online node with capacity, without publishing anything.</summary>
    private static async Task<Guid> SeedNodeAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var utcNow = DateTime.UtcNow;
        var node = new Node { Id = Guid.NewGuid(), OrganizationId = OrgId, Name = "outbox-node", Status = NodeStatus.Online, Platform = "linux", CreatedAt = utcNow };
        context.Nodes.Add(node);
        context.HardwareInventories.Add(new NodeHardwareInventory { Id = Guid.NewGuid(), NodeId = node.Id, Hostname = "test-host", CpuCores = 8, MemoryBytes = 32L * 1024 * 1024 * 1024, DiskBytes = 1000L * 1024 * 1024 * 1024, CollectedAt = utcNow });
        context.NodeCapacities.Add(new NodeCapacity { Id = Guid.NewGuid(), NodeId = node.Id, MaxGameServers = 10, CurrentGameServers = 2, AvailableMemoryBytes = 16L * 1024 * 1024 * 1024, AvailableDiskBytes = 500L * 1024 * 1024 * 1024, UpdatedAt = utcNow });
        await context.SaveChangesAsync();
        return node.Id;
    }

    /// <summary>A named shared-cache in-memory database that lives while the keep-alive connection is open.</summary>
    private sealed class OutboxDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _keepAlive;

        private OutboxDatabase(string connectionString, SqliteConnection keepAlive)
        {
            ConnectionString = connectionString;
            _keepAlive = keepAlive;
        }

        public string ConnectionString { get; }

        public static async Task<OutboxDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=nodes-outbox-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(connectionString);
            await keepAlive.OpenAsync();
            return new OutboxDatabase(connectionString, keepAlive);
        }

        public ValueTask DisposeAsync() => _keepAlive.DisposeAsync();
    }

    /// <summary>Test-only consumer: proves the outbox delivered the event (harness.Published does not see outbox deliveries, F232).</summary>
    // CA1812: MassTransit instantiates this consumer at runtime (AddConsumer / GetConsumerHarness); the analyzer cannot see that.
#pragma warning disable CA1812
    internal sealed class CapacityReservedProbe : IConsumer<CapacityReserved>
    {
        public Task Consume(ConsumeContext<CapacityReserved> context) => Task.CompletedTask;
    }
#pragma warning restore CA1812

    /// <summary>Counts OutboxMessage rows added by saves that succeed, per context.</summary>
    private sealed class OutboxWriteCounter : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<Guid, int> _pending = new();
        private int _saved;

        public int Saved => Volatile.Read(ref _saved);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            _pending[context.ContextId.InstanceId] =
                context.ChangeTracker.Entries<OutboxMessage>().Count(e => e.State == EntityState.Added);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_pending.TryRemove(eventData.Context!.ContextId.InstanceId, out var added))
            {
                Interlocked.Add(ref _saved, added);
            }

            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            _pending.TryRemove(eventData.Context!.ContextId.InstanceId, out _);
            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }
    }

    /// <summary>While armed, fails every save that carries a reservation with a non-transient DbUpdateException (not the delivery service's saves).</summary>
    private sealed class FailSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<CapacityReservation>().Any())
            {
                throw new DbUpdateException("Simulated save failure", (Exception?)null);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
