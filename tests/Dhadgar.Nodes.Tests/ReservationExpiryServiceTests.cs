using Dhadgar.Nodes.BackgroundServices;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(5);

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            CreateOptions(),
            NullLogger<ReservationExpiryService>.Instance);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait a bit for execution to happen
        await Task.Delay(100);
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
                callCount++;
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

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            options,
            NullLogger<ReservationExpiryService>.Instance);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for call or timeout after 2 seconds
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
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
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(0);

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        using var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        using var cts = new CancellationTokenSource();
        using var service = new ReservationExpiryService(
            scopeFactory,
            CreateOptions(60), // 60 minute interval
            NullLogger<ReservationExpiryService>.Instance);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Cancel immediately after start
        await Task.Delay(50);
        await cts.CancelAsync();

        // Assert - should complete without timeout
        var completedTask = await Task.WhenAny(executeTask, Task.Delay(1000));
        Assert.Equal(executeTask, completedTask);
    }
}
