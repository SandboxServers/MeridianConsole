using System.Collections.Concurrent;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using Dhadgar.Servers.Services;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Servers.Tests.Services;

public sealed class ServerLifecycleServiceTests : IDisposable
{
    private readonly ServersDbContext _db;
    private readonly TestPublishEndpoint _publisher;
    private readonly ServerLifecycleService _service;
    private readonly Guid _orgId = Guid.NewGuid();

    public ServerLifecycleServiceTests()
    {
        var efProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ServersDbContext>()
            .UseInMemoryDatabase($"lifecycle-tests-{Guid.NewGuid()}")
            .UseInternalServiceProvider(efProvider)
            .Options;

        _db = new ServersDbContext(options);
        _publisher = new TestPublishEndpoint();
        _service = new ServerLifecycleService(
            _db,
            _publisher,
            NullLogger<ServerLifecycleService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<Server> CreateServerAsync(
        ServerStatus status,
        ServerPowerState powerState,
        Guid? nodeId = null,
        DateTime? lastStartedAt = null)
    {
        var server = new Server
        {
            OrganizationId = _orgId,
            Name = $"test-{Guid.NewGuid():N}"[..16],
            GameType = "minecraft",
            CpuLimitMillicores = 2000,
            MemoryLimitMb = 4096,
            DiskLimitMb = 10240,
            Status = status,
            PowerState = powerState,
            NodeId = nodeId,
            LastStartedAt = lastStartedAt
        };

        _db.Servers.Add(server);
        await _db.SaveChangesAsync();
        return server;
    }

    // --- TransitionStatus: Valid Transitions ---

    [Theory]
    [InlineData(ServerStatus.Created, ServerStatus.Provisioning)]
    [InlineData(ServerStatus.Created, ServerStatus.Deleted)]
    [InlineData(ServerStatus.Provisioning, ServerStatus.Installing)]
    [InlineData(ServerStatus.Provisioning, ServerStatus.Error)]
    [InlineData(ServerStatus.Installing, ServerStatus.Ready)]
    [InlineData(ServerStatus.Installing, ServerStatus.Error)]
    [InlineData(ServerStatus.Ready, ServerStatus.Starting)]
    [InlineData(ServerStatus.Ready, ServerStatus.Maintenance)]
    [InlineData(ServerStatus.Ready, ServerStatus.Suspended)]
    [InlineData(ServerStatus.Starting, ServerStatus.Running)]
    [InlineData(ServerStatus.Starting, ServerStatus.Crashed)]
    [InlineData(ServerStatus.Starting, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Running, ServerStatus.Stopping)]
    [InlineData(ServerStatus.Running, ServerStatus.Crashed)]
    [InlineData(ServerStatus.Stopping, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Stopping, ServerStatus.Crashed)]
    [InlineData(ServerStatus.Stopped, ServerStatus.Starting)]
    [InlineData(ServerStatus.Stopped, ServerStatus.Ready)]
    [InlineData(ServerStatus.Crashed, ServerStatus.Starting)]
    [InlineData(ServerStatus.Crashed, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Error, ServerStatus.Ready)]
    [InlineData(ServerStatus.Error, ServerStatus.Maintenance)]
    [InlineData(ServerStatus.Suspended, ServerStatus.Ready)]
    [InlineData(ServerStatus.Suspended, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Maintenance, ServerStatus.Ready)]
    [InlineData(ServerStatus.Maintenance, ServerStatus.Stopped)]
    public async Task TransitionStatus_ValidTransition_Succeeds(ServerStatus from, ServerStatus to)
    {
        var server = await CreateServerAsync(from, ServerPowerState.Off);

        var result = await _service.TransitionStatusAsync(_orgId, server.Id, to);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(ServerStatus.Created, ServerStatus.Running)]
    [InlineData(ServerStatus.Created, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Ready, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Running, ServerStatus.Ready)]
    [InlineData(ServerStatus.Running, ServerStatus.Deleted)]
    [InlineData(ServerStatus.Stopping, ServerStatus.Starting)]
    [InlineData(ServerStatus.Stopped, ServerStatus.Running)]
    [InlineData(ServerStatus.Crashed, ServerStatus.Running)]
    [InlineData(ServerStatus.Suspended, ServerStatus.Running)]
    public async Task TransitionStatus_InvalidTransition_ReturnsFailure(ServerStatus from, ServerStatus to)
    {
        var server = await CreateServerAsync(from, ServerPowerState.Off);

        var result = await _service.TransitionStatusAsync(_orgId, server.Id, to);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("invalid_transition");
    }

    [Fact]
    public async Task TransitionStatus_ServerNotFound_ReturnsFailure()
    {
        var result = await _service.TransitionStatusAsync(_orgId, Guid.NewGuid(), ServerStatus.Ready);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("server_not_found");
    }

    // --- PowerState Updates ---

    [Fact]
    public async Task TransitionStatus_ToStarting_SetsPowerStateStarting()
    {
        var server = await CreateServerAsync(ServerStatus.Ready, ServerPowerState.Off);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Starting);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.PowerState.Should().Be(ServerPowerState.Starting);
    }

    [Fact]
    public async Task TransitionStatus_ToRunning_SetsPowerStateOn()
    {
        var server = await CreateServerAsync(ServerStatus.Starting, ServerPowerState.Starting);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Running);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.PowerState.Should().Be(ServerPowerState.On);
    }

    [Fact]
    public async Task TransitionStatus_ToStopping_SetsPowerStateStopping()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Stopping);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.PowerState.Should().Be(ServerPowerState.Stopping);
    }

    [Fact]
    public async Task TransitionStatus_ToCrashed_SetsPowerStateCrashed()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Crashed);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.PowerState.Should().Be(ServerPowerState.Crashed);
    }

    [Fact]
    public async Task TransitionStatus_ToStopped_SetsPowerStateOff()
    {
        var server = await CreateServerAsync(ServerStatus.Stopping, ServerPowerState.Stopping);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Stopped);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.PowerState.Should().Be(ServerPowerState.Off);
    }

    // --- Timestamp and Counter Updates ---

    [Fact]
    public async Task TransitionStatus_ToRunning_SetsLastStartedAt()
    {
        var server = await CreateServerAsync(ServerStatus.Starting, ServerPowerState.Starting);
        server.LastStartedAt.Should().BeNull();

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Running);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.LastStartedAt.Should().NotBeNull();
        updated.LastStartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TransitionStatus_ToStopped_CalculatesUptime()
    {
        var startTime = DateTime.UtcNow.AddMinutes(-10);
        var server = await CreateServerAsync(ServerStatus.Stopping, ServerPowerState.Stopping, lastStartedAt: startTime);
        server.TotalUptimeSeconds.Should().Be(0);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Stopped);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.LastStoppedAt.Should().NotBeNull();
        updated.TotalUptimeSeconds.Should().BeGreaterThan(0);
        // At least ~600 seconds (10 min) of uptime, allow tolerance
        updated.TotalUptimeSeconds.Should().BeGreaterThanOrEqualTo(590);
    }

    [Fact]
    public async Task TransitionStatus_ToStopped_ClearsLastStartedAt()
    {
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var server = await CreateServerAsync(ServerStatus.Stopping, ServerPowerState.Stopping, lastStartedAt: startTime);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Stopped);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.LastStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task TransitionStatus_ToCrashed_IncrementsCrashCount()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On);
        server.CrashCount.Should().Be(0);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Crashed);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.CrashCount.Should().Be(1);
    }

    [Fact]
    public async Task TransitionStatus_ToCrashedMultipleTimes_CumulativeCount()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On, Guid.NewGuid());

        // Crash 1: Running -> Crashed
        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Crashed);

        // Recover: Crashed -> Starting
        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Starting);

        // Start: Starting -> Running
        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Running);

        // Crash 2: Running -> Crashed
        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Crashed);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.CrashCount.Should().Be(2);
    }

    [Fact]
    public async Task TransitionStatus_ToCrashed_SetsLastStoppedAt()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On);

        await _service.TransitionStatusAsync(_orgId, server.Id, ServerStatus.Crashed);

        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.LastStoppedAt.Should().NotBeNull();
    }

    // --- Start/Stop/Restart/Kill Service Methods ---

    [Fact]
    public async Task StartServer_FromReady_WithNode_Succeeds()
    {
        var nodeId = Guid.NewGuid();
        var server = await CreateServerAsync(ServerStatus.Ready, ServerPowerState.Off, nodeId);

        var result = await _service.StartServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Status.Should().Be(ServerStatus.Starting);
        updated.PowerState.Should().Be(ServerPowerState.Starting);
    }

    [Fact]
    public async Task StartServer_WithoutNode_Fails()
    {
        var server = await CreateServerAsync(ServerStatus.Ready, ServerPowerState.Off, nodeId: null);

        var result = await _service.StartServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("server_not_placed");
    }

    [Fact]
    public async Task StopServer_FromRunning_Succeeds()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On, Guid.NewGuid());

        var result = await _service.StopServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Status.Should().Be(ServerStatus.Stopping);
        updated.PowerState.Should().Be(ServerPowerState.Stopping);
    }

    [Fact]
    public async Task RestartServer_FromRunning_Succeeds()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On, Guid.NewGuid());

        var result = await _service.RestartServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task KillServer_PowerStateOff_Fails()
    {
        var server = await CreateServerAsync(ServerStatus.Stopped, ServerPowerState.Off);

        var result = await _service.KillServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("server_already_off");
    }

    [Fact]
    public async Task KillServer_PowerStateOn_SetsStoppedAndOff()
    {
        var server = await CreateServerAsync(ServerStatus.Running, ServerPowerState.On, Guid.NewGuid());

        var result = await _service.KillServerAsync(_orgId, server.Id);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.Servers.FindAsync(server.Id);
        updated!.Status.Should().Be(ServerStatus.Stopped);
        updated.PowerState.Should().Be(ServerPowerState.Off);
        updated.LastStoppedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Minimal test-only IPublishEndpoint that captures published messages.
    /// </summary>
    private sealed class TestPublishEndpoint : IPublishEndpoint
    {
        private readonly ConcurrentQueue<object> _messages = new();
        public IReadOnlyList<object> Messages => _messages.ToList();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(values);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(values);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            _messages.Enqueue(values);
            return Task.CompletedTask;
        }

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => new NoOpHandle();

        private sealed class NoOpHandle : ConnectHandle
        {
            public void Disconnect() { }
            public void Dispose() { }
        }
    }
}

