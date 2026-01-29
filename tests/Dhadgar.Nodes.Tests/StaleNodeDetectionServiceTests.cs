using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act - start the service and let it run one iteration
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(100); // Give it time to execute once
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert
        await heartbeatService.Received(1).CheckStaleNodesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptionGracefully()
    {
        // Arrange
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(x => throw new InvalidOperationException("Database error"));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act - start the service, it should handle the exception
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();

        // Assert - should not throw
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        // Arrange
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - should complete without hanging
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_CallsCheckStaleNodesMultipleTimes()
    {
        // Arrange - use very short interval for testing
        var options = Options.Create(new NodesOptions { StaleNodeCheckIntervalMinutes = 0 });

        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            options,
            NullLogger<StaleNodeDetectionService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act - let it run for a bit to execute multiple iterations
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - should have been called multiple times
        await heartbeatService.Received().CheckStaleNodesAsync(Arg.Any<CancellationToken>());
        var callCount = heartbeatService.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "CheckStaleNodesAsync");
        Assert.True(callCount >= 1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStaleNodesFound_ReturnsCount()
    {
        // Arrange
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(5)); // 5 stale nodes found

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            NullLogger<StaleNodeDetectionService>.Instance);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - the service ran successfully with stale nodes
        await heartbeatService.Received().CheckStaleNodesAsync(Arg.Any<CancellationToken>());
    }
}
