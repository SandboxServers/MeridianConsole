using System.Globalization;
using System.Threading;
using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests;

public sealed class StaleNodeDetectionServiceTests
{
    private static IOptions<NodesOptions> CreateOptions(int checkIntervalMinutes = 1) =>
        Options.Create(new NodesOptions { StaleNodeCheckIntervalMinutes = checkIntervalMinutes });

    [Fact]
    public async Task ExecuteAsync_CallsCheckStaleNodesAsync()
    {
        // Arrange
        var callTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return Task.FromResult(0);
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act - start the service and let it run one iteration
        _ = service.StartAsync(cts.Token);

        // Wait for the first call to happen
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert
        await heartbeatService.Received(1).CheckStaleNodesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptionGracefully()
    {
        // Arrange
        var callTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(x =>
            {
                callTcs.TrySetResult();
                throw new InvalidOperationException("Database error");
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act - start the service, it should handle the exception
        _ = service.StartAsync(cts.Token);

        // Wait for the call to happen
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        // Assert - should not throw
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        // Arrange
        var callTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return Task.FromResult(0);
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);

        // Wait for the first call
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        // Assert - StopAsync should complete within a reasonable timeout
        var stopTask = service.StopAsync(CancellationToken.None);
        var completedTask = await Task.WhenAny(stopTask, Task.Delay(5000));
        Assert.True(completedTask == stopTask, "StopAsync did not complete within the expected timeout");
    }

    [Fact]
    public async Task ExecuteAsync_CallsCheckStaleNodesOnStartup()
    {
        // Arrange - use minimum valid interval (1 minute) for testing
        var options = Options.Create(new NodesOptions { StaleNodeCheckIntervalMinutes = 1 });

        var callTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return Task.FromResult(0);
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            options,
            NullLogger<StaleNodeDetectionService>.Instance,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act - start and let it execute initial check
        await service.StartAsync(cts.Token);

        // Wait for the call to happen
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - should have been called at least once on startup
        await heartbeatService.Received().CheckStaleNodesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStaleNodesFound_LogsCount()
    {
        // Arrange
        const int expectedStaleCount = 5;
        var callTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return Task.FromResult(expectedStaleCount);
            }); // 5 stale nodes found

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var mockLogger = Substitute.For<ILogger<StaleNodeDetectionService>>();
        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            mockLogger,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);

        // Wait for the call to happen
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - verify the logger received a call that includes the count
        mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains(expectedStaleCount.ToString(CultureInfo.InvariantCulture))),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_AdvancingTime_TriggersNextIteration()
    {
        // Arrange
        var callCount = 0;
        var firstCallTcs = new TaskCompletionSource();
        var secondCallTcs = new TaskCompletionSource();
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount == 1)
                    firstCallTcs.TrySetResult();
                else if (callCount == 2)
                    secondCallTcs.TrySetResult();
                return Task.FromResult(0);
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(1), // 1 minute interval
            NullLogger<StaleNodeDetectionService>.Instance,
            fakeTimeProvider);

        using var cts = new CancellationTokenSource();

        // Act
        _ = service.StartAsync(cts.Token);

        // Wait for first call (happens immediately)
        await firstCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, callCount);

        // Advance time past the check interval to trigger second iteration
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));

        // Wait for second call
        await secondCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, callCount);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }
}
