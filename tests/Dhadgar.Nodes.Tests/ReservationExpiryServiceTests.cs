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
        var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var cts = new CancellationTokenSource();
        var service = new ReservationExpiryService(
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
    public async Task ExecuteAsync_ContinuesOnError()
    {
        // Arrange
        var callCount = 0;
        var tcs = new TaskCompletionSource();
        var mockReservationService = Substitute.For<ICapacityReservationService>();
        mockReservationService
            .ExpireStaleReservationsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Test error");
                }
                if (callCount >= 2)
                {
                    tcs.TrySetResult();
                }
                return Task.FromResult(0);
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockReservationService);
        var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        // Use a very short interval for testing (1ms equivalent via NodesOptions)
        var options = Options.Create(new NodesOptions
        {
            ReservationExpiryCheckIntervalMinutes = 0 // Will result in minimum TimeSpan
        });

        var cts = new CancellationTokenSource();
        var service = new ReservationExpiryService(
            scopeFactory,
            options,
            NullLogger<ReservationExpiryService>.Instance);

        // Act
        var executeTask = service.StartAsync(cts.Token);

        // Wait for second call or timeout after 2 seconds
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

        // Assert - should have been called at least twice despite the first error
        Assert.True(callCount >= 2, $"Expected at least 2 calls but got {callCount}");
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
        var serviceProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var cts = new CancellationTokenSource();
        var service = new ReservationExpiryService(
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
