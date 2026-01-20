using Dhadgar.Contracts.Notifications;
using Dhadgar.Discord.Consumers;
using Dhadgar.Discord.Data;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dhadgar.Discord.Tests.Consumers;

public sealed class SendDiscordNotificationConsumerTests : IDisposable
{
    private readonly DiscordDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendDiscordNotificationConsumer> _logger;

    public SendDiscordNotificationConsumerTests()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseInMemoryDatabase($"DiscordConsumerTest_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DiscordDbContext(options);
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _configuration = Substitute.For<IConfiguration>();
        _logger = Substitute.For<ILogger<SendDiscordNotificationConsumer>>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Consume_WithNoWebhookConfigured_DoesNotThrow()
    {
        // Arrange - No webhook URL configured
        _configuration["Discord:WebhookUrl"].Returns((string?)null);

        var consumer = new SendDiscordNotificationConsumer(
            _dbContext, _httpClientFactory, _configuration, _logger);

        var notification = new SendDiscordNotification(
            NotificationId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            ServerId: Guid.NewGuid(),
            Title: "Test Server Started",
            Message: "Server 'test-server' is now online",
            Severity: NotificationSeverity.Info,
            EventType: NotificationEventTypes.ServerStarted,
            Fields: new Dictionary<string, string> { ["Player Count"] = "0" },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        var context = Substitute.For<ConsumeContext<SendDiscordNotification>>();
        context.Message.Returns(notification);
        context.CancellationToken.Returns(CancellationToken.None);

        // Act - Should complete without throwing
        await consumer.Consume(context);

        // Assert - Nothing logged to DB when no webhook configured
        var logs = await _dbContext.NotificationLogs.ToListAsync();
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_WithWebhookConfigured_LogsNotificationToDatabase()
    {
        // Arrange
        _configuration["Discord:WebhookUrl"].Returns("https://discord.com/api/webhooks/test");

        // Mock HTTP client that returns success
        var mockHttpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.NoContent));
        _httpClientFactory.CreateClient().Returns(mockHttpClient);

        var consumer = new SendDiscordNotificationConsumer(
            _dbContext, _httpClientFactory, _configuration, _logger);

        var notification = new SendDiscordNotification(
            NotificationId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            ServerId: Guid.NewGuid(),
            Title: "Test Server Started",
            Message: "Server 'test-server' is now online",
            Severity: NotificationSeverity.Info,
            EventType: NotificationEventTypes.ServerStarted,
            Fields: new Dictionary<string, string> { ["Player Count"] = "0" },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        var context = Substitute.For<ConsumeContext<SendDiscordNotification>>();
        context.Message.Returns(notification);
        context.CancellationToken.Returns(CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        var logs = await _dbContext.NotificationLogs.ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].EventType.Should().Be(notification.EventType);
        logs[0].Title.Should().Be(notification.Title);
        logs[0].Status.Should().Be("sent");
    }

    [Fact]
    public async Task Consume_WhenWebhookFails_LogsAsFailedStatus()
    {
        // Arrange
        _configuration["Discord:WebhookUrl"].Returns("https://discord.com/api/webhooks/test");

        // Mock HTTP client that returns error
        var mockHttpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.BadRequest));
        _httpClientFactory.CreateClient().Returns(mockHttpClient);

        var consumer = new SendDiscordNotificationConsumer(
            _dbContext, _httpClientFactory, _configuration, _logger);

        var notification = new SendDiscordNotification(
            NotificationId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            ServerId: null,
            Title: "Test Notification",
            Message: "This is a test",
            Severity: NotificationSeverity.Warning,
            EventType: NotificationEventTypes.ResourceWarning,
            Fields: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        var context = Substitute.For<ConsumeContext<SendDiscordNotification>>();
        context.Message.Returns(notification);
        context.CancellationToken.Returns(CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        var log = await _dbContext.NotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.Status.Should().Be("failed");
        log.ErrorMessage.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task Consume_CategorizesChannelByEventSeverity()
    {
        // Arrange
        _configuration["Discord:WebhookUrl"].Returns("https://discord.com/api/webhooks/test");

        var mockHttpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.NoContent));
        _httpClientFactory.CreateClient().Returns(mockHttpClient);

        var consumer = new SendDiscordNotificationConsumer(
            _dbContext, _httpClientFactory, _configuration, _logger);

        // Critical severity notification
        var notification = new SendDiscordNotification(
            NotificationId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            ServerId: Guid.NewGuid(),
            Title: "Server Crashed",
            Message: "Critical failure",
            Severity: NotificationSeverity.Critical,
            EventType: NotificationEventTypes.ServerCrashed,
            Fields: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        var context = Substitute.For<ConsumeContext<SendDiscordNotification>>();
        context.Message.Returns(notification);
        context.CancellationToken.Returns(CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        var log = await _dbContext.NotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.Channel.Should().Be("alerts"); // Critical severity -> alerts channel
    }

    /// <summary>
    /// Simple HTTP message handler mock for testing.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _statusCode;

        public MockHttpMessageHandler(System.Net.HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("")
            });
        }
    }
}
