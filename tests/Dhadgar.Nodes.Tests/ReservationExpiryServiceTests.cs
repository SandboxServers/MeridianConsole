using System.Threading;
using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dhadgar.Nodes.Tests;

public sealed class ReservationExpiryServiceTests
{
    private static IOptions<NodesOptions> CreateOptions(int intervalMinutes = 1) =>
        Options.Create(new NodesOptions
        {
            ReservationExpiryCheckIntervalMinutes = intervalMinutes
        });

    [Fact]
    public async Task ExecuteAsync_CallsExpireStaleReservations()
    {
        // Arrange
        var callTcs = new TaskCompletionSource();
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return 5;
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            CreateOptions(),
            NullLogger<ReservationExpiryService>.Instance,
            fakeTimeProvider);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for the first call to happen (the service calls immediately on startup)
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        await mockReservationService.Received()
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HandlesErrorGracefully()
    {
        // Arrange
        var callCount = 0;
        var tcs = new TaskCompletionSource();
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ =>
            {
                Interlocked.Increment(ref callCount);
                tcs.TrySetResult();
                // Throw error to verify service doesn't crash
                throw new InvalidOperationException("Test error");
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        // Use the minimum valid interval for testing
        var options = Options.Create(new NodesOptions
        {
            ReservationExpiryCheckIntervalMinutes = 1 // Minimum valid value per [Range(1, int.MaxValue)]
        });

        var fakeTimeProvider = new FakeTimeProvider();

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            options,
            NullLogger<ReservationExpiryService>.Instance,
            fakeTimeProvider);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for call (the service calls immediately on startup)
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - service should have attempted the operation (didn't crash on error)
        Assert.True(callCount >= 1, $"Expected at least 1 call but got {callCount}");
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        // Arrange
        var callTcs = new TaskCompletionSource();
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callTcs.TrySetResult();
                return 0;
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            CreateOptions(60), // 60 minute interval
            NullLogger<ReservationExpiryService>.Instance,
            fakeTimeProvider);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for the first call to happen (service is now in delay)
        await callTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel while waiting
        await cts.CancelAsync();

        // Assert - should complete without timeout
        var completedTask = await Task.WhenAny(executeTask, Task.Delay(1000));
        Assert.Equal(executeTask, completedTask);
    }

    [Fact]
    public async Task ExecuteAsync_AdvancingTime_TriggersNextIteration()
    {
        // Arrange
        var callCount = 0;
        var firstCallTcs = new TaskCompletionSource();
        var secondCallTcs = new TaskCompletionSource();
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 1)
                    firstCallTcs.TrySetResult();
                else if (count == 2)
                    secondCallTcs.TrySetResult();
                return 0;
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var fakeTimeProvider = new FakeTimeProvider();

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            CreateOptions(1), // 1 minute interval
            NullLogger<ReservationExpiryService>.Instance,
            fakeTimeProvider);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for first call (happens immediately)
        await firstCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, callCount);

        // Brief real delay to ensure the service loop has reached Task.Delay
        // This prevents a race condition when tests run in parallel with high CPU contention
        await Task.Delay(10);

        // Advance time past the check interval to trigger second iteration
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));

        // Wait for second call
        await secondCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, callCount);

        await cts.CancelAsync();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }
}
