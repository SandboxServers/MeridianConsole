using Dhadgar.Notifications.Alerting;
using Dhadgar.Notifications.Discord;
using Dhadgar.Notifications.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dhadgar.Notifications.Tests.Alerting;

public sealed class AlertDispatcherTests
{
    private readonly IDiscordWebhook _mockDiscord;
    private readonly IEmailSender _mockEmail;
    private readonly AlertThrottler _throttler;
    private readonly AlertDispatcher _dispatcher;

    public AlertDispatcherTests()
    {
        _mockDiscord = Substitute.For<IDiscordWebhook>();
        _mockEmail = Substitute.For<IEmailSender>();
        _throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        _dispatcher = new AlertDispatcher(
            _mockDiscord,
            _mockEmail,
            _throttler,
            NullLogger<AlertDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_SendsToDiscordAndEmail()
    {
        // Arrange
        var alert = CreateAlert();

        // Act
        await _dispatcher.DispatchAsync(alert);

        // Assert
        await _mockDiscord.Received(1).SendAlertAsync(alert, Arg.Any<CancellationToken>());
        await _mockEmail.Received(1).SendAlertEmailAsync(alert, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_ThrottledAlert_DoesNotSend()
    {
        // Arrange
        var alert = CreateAlert();

        // Act
        await _dispatcher.DispatchAsync(alert); // First call
        await _dispatcher.DispatchAsync(alert); // Throttled

        // Assert - Only called once (first dispatch, second throttled)
        await _mockDiscord.Received(1).SendAlertAsync(alert, Arg.Any<CancellationToken>());
        await _mockEmail.Received(1).SendAlertEmailAsync(alert, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_DifferentAlerts_SendsBoth()
    {
        // Arrange
        var alert1 = CreateAlert("Service1");
        var alert2 = CreateAlert("Service2");

        // Act
        await _dispatcher.DispatchAsync(alert1);
        await _dispatcher.DispatchAsync(alert2);

        // Assert
        await _mockDiscord.Received(2).SendAlertAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
        await _mockEmail.Received(2).SendAlertEmailAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_DiscordFails_ContinuesWithOtherChannels()
    {
        // Arrange
        var alert = CreateAlert();
        _mockDiscord
            .SendAlertAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Discord unreachable")));

        // Act - AlertDispatcher intentionally isolates channel failures
        // so one channel failing doesn't prevent other channels from receiving alerts
        Func<Task> act = () => _dispatcher.DispatchAsync(alert);

        // Assert - No exception should propagate (fire-and-forget alerting design)
        await act.Should().NotThrowAsync();
        // Email should still have been called despite Discord failure
        await _mockEmail.Received(1).SendAlertEmailAsync(alert, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_AlertWithTraceContext_PassesToChannels()
    {
        // Arrange
        var alert = new AlertMessage
        {
            Title = "Test Alert",
            Message = "Test message",
            Severity = AlertSeverity.Error,
            ServiceName = "TestService",
            TraceId = "abc123",
            CorrelationId = "corr456"
        };

        // Act
        await _dispatcher.DispatchAsync(alert);

        // Assert
        await _mockDiscord.Received(1).SendAlertAsync(
            Arg.Is<AlertMessage>(a => a.TraceId == "abc123" && a.CorrelationId == "corr456"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_MultipleDistinctAlerts_AllDispatched()
    {
        // Arrange
        var alerts = new[]
        {
            CreateAlert("Service1", "Alert A"),
            CreateAlert("Service1", "Alert B"),
            CreateAlert("Service2", "Alert A")
        };

        // Act
        foreach (var alert in alerts)
        {
            await _dispatcher.DispatchAsync(alert);
        }

        // Assert - All 3 should be dispatched (different keys)
        await _mockDiscord.Received(3).SendAlertAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
        await _mockEmail.Received(3).SendAlertEmailAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_SameAlertDifferentExceptionTypes_BothDispatched()
    {
        // Arrange - Exception type is part of throttle key
        var alert1 = new AlertMessage
        {
            Title = "Error",
            Message = "Test message",
            Severity = AlertSeverity.Error,
            ServiceName = "TestService",
            ExceptionType = "NullReferenceException"
        };
        var alert2 = new AlertMessage
        {
            Title = "Error",
            Message = "Test message",
            Severity = AlertSeverity.Error,
            ServiceName = "TestService",
            ExceptionType = "ArgumentException"
        };

        // Act
        await _dispatcher.DispatchAsync(alert1);
        await _dispatcher.DispatchAsync(alert2);

        // Assert - Both dispatched (different exception types = different keys)
        await _mockDiscord.Received(2).SendAlertAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
        await _mockEmail.Received(2).SendAlertEmailAsync(Arg.Any<AlertMessage>(), Arg.Any<CancellationToken>());
    }

    private static AlertMessage CreateAlert(string serviceName = "TestService", string title = "Test Alert")
    {
        return new AlertMessage
        {
            Title = title,
            Message = "This is a test alert message",
            Severity = AlertSeverity.Error,
            ServiceName = serviceName
        };
    }
}
