using Dhadgar.Notifications.Alerting;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Notifications.Tests.Alerting;

public sealed class AlertThrottlerTests
{
    [Fact]
    public void ShouldSend_FirstAlert_ReturnsTrue()
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert = CreateAlert("TestService", "Test Alert");

        // Act
        var result = throttler.ShouldSend(alert);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_DuplicateWithinWindow_ReturnsFalse()
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert = CreateAlert("TestService", "Test Alert");

        // Act
        throttler.ShouldSend(alert); // First call
        var result = throttler.ShouldSend(alert); // Duplicate within window

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldSend_DifferentAlerts_BothReturnTrue()
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert1 = CreateAlert("Service1", "Alert 1");
        var alert2 = CreateAlert("Service2", "Alert 2");

        // Act
        var result1 = throttler.ShouldSend(alert1);
        var result2 = throttler.ShouldSend(alert2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_SameServiceDifferentTitle_BothReturnTrue()
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert1 = CreateAlert("TestService", "Alert Type A");
        var alert2 = CreateAlert("TestService", "Alert Type B");

        // Act
        var result1 = throttler.ShouldSend(alert1);
        var result2 = throttler.ShouldSend(alert2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_SameAlertDifferentException_BothReturnTrue()
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert1 = CreateAlert("TestService", "Error", "NullReferenceException");
        var alert2 = CreateAlert("TestService", "Error", "ArgumentException");

        // Act
        var result1 = throttler.ShouldSend(alert1);
        var result2 = throttler.ShouldSend(alert2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_AfterWindowExpires_ReturnsTrue()
    {
        // Arrange - Use very short window for testing
        var throttler = new AlertThrottler(TimeSpan.FromMilliseconds(50));
        var alert = CreateAlert("TestService", "Test Alert");

        // Act
        throttler.ShouldSend(alert); // First call
        Thread.Sleep(100); // Wait for window to expire
        var result = throttler.ShouldSend(alert); // After window

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(AlertSeverity.Warning)]
    [InlineData(AlertSeverity.Error)]
    [InlineData(AlertSeverity.Critical)]
    public void ShouldSend_AllSeverities_FirstAlertReturnsTrue(AlertSeverity severity)
    {
        // Arrange
        var throttler = new AlertThrottler(TimeSpan.FromMinutes(5));
        var alert = new AlertMessage
        {
            Title = "Test Alert",
            Message = "Test message",
            Severity = severity,
            ServiceName = "TestService"
        };

        // Act
        var result = throttler.ShouldSend(alert);

        // Assert
        result.Should().BeTrue();
    }

    private static AlertMessage CreateAlert(
        string serviceName,
        string title,
        string? exceptionType = null)
    {
        return new AlertMessage
        {
            Title = title,
            Message = $"Test message for {title}",
            Severity = AlertSeverity.Error,
            ServiceName = serviceName,
            ExceptionType = exceptionType
        };
    }
}
