using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Health;

public sealed class HeartbeatServiceTests
{
    /// <summary>
    /// Regression test for #101: the heartbeat loop used to wait on <see cref="AgentOptions.NodeId"/>, which
    /// never gets set at runtime, so the agent never sent a heartbeat. It must start once the identity is set.
    /// </summary>
    [Fact]
    public async Task Heartbeat_StartsAfterIdentityIsSet_WithoutOptionsNodeId()
    {
        var identity = new AgentIdentityProvider();
        var client = new FakeControlPlaneClient();
        using var sut = new HeartbeatService(
            client,
            new FakeHealthReporter(),
            identity,
            new StaticOptionsMonitor(new AgentOptions()),
            NullLogger<HeartbeatService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        try
        {
            // The first heartbeat follows a 2 s startup delay after the identity is set.
            identity.Set(new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

            await client.FirstHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        Assert.True(client.HeartbeatCount > 0);
    }

    private sealed class StaticOptionsMonitor(AgentOptions value) : IOptionsMonitor<AgentOptions>
    {
        public AgentOptions CurrentValue => value;

        public AgentOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AgentOptions, string?> listener) => null;
    }

    private sealed class FakeHealthReporter : IHealthReporter
    {
        public NodeStatus Status => NodeStatus.Online;

        public void SetStatus(NodeStatus status, string? reason = null)
        {
        }

        public void AddWarning(string warning)
        {
        }

        public void ClearWarnings()
        {
        }

        public Task<Result<HeartbeatPayload>> GetHeartbeatPayloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HeartbeatPayload>.Success(new HeartbeatPayload
            {
                NodeId = Guid.NewGuid(),
                AgentVersion = "test",
            }));
    }

    private sealed class FakeControlPlaneClient : IControlPlaneClient
    {
        private int _heartbeatCount;

        /// <summary>Completes on the first heartbeat sent.</summary>
        public TaskCompletionSource FirstHeartbeat { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HeartbeatCount => Volatile.Read(ref _heartbeatCount);

        public ConnectionState State => ConnectionState.Connected;

        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<CommandReceivedEventArgs>? CommandReceived
        {
            add { }
            remove { }
        }

        public Task<Result> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<AgentHeartbeatAck>> SendHeartbeatAsync(
            AgentHeartbeat heartbeat,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _heartbeatCount);
            FirstHeartbeat.TrySetResult();
            return Task.FromResult(Result<AgentHeartbeatAck>.Success(new AgentHeartbeatAck(true, DateTimeOffset.UtcNow)));
        }

        public Task<bool> SendCommandResultAsync(CommandResult result, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SendTelemetryAsync(TelemetryPayload payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
