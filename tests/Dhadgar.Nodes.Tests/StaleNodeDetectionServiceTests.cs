using System.Globalization;
using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        // Assert - StopAsync should complete within a reasonable timeout
        var stopTask = service.StopAsync(CancellationToken.None);
        var completedTask = await Task.WhenAny(stopTask, Task.Delay(5000));
        Assert.True(completedTask == stopTask, "StopAsync did not complete within the expected timeout");
    }

    [Fact]
    public async Task ExecuteAsync_CallsCheckStaleNodesOnStartup()
    {
        // Arrange - use minimum valid interval (1 minute) for testing
        // Note: With 1-minute interval, we can only verify initial call in a reasonable test time
        var options = Options.Create(new NodesOptions { StaleNodeCheckIntervalMinutes = 1 });

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

        // Act - start and let it execute initial check
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert - should have been called at least once on startup
        await heartbeatService.Received().CheckStaleNodesAsync(Arg.Any<CancellationToken>());
        var callCount = heartbeatService.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "CheckStaleNodesAsync");
        Assert.True(callCount >= 1, $"Expected at least 1 call, but got {callCount}");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStaleNodesFound_LogsCount()
    {
        // Arrange
        const int expectedStaleCount = 5;
        var heartbeatService = Substitute.For<IHeartbeatService>();
        heartbeatService.CheckStaleNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedStaleCount)); // 5 stale nodes found

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IHeartbeatService)).Returns(heartbeatService);

        var serviceScope = Substitute.For<IServiceScope>();
        serviceScope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(serviceScope);

        var mockLogger = Substitute.For<ILogger<StaleNodeDetectionService>>();

        using var service = new StaleNodeDetectionService(
            scopeFactory,
            CreateOptions(),
            mockLogger);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
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
}
