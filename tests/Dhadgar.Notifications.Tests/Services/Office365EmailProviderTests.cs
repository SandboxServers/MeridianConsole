using Dhadgar.Notifications.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dhadgar.Notifications.Tests.Services;

public sealed class Office365EmailProviderTests
{
    [Fact]
    public void Constructor_ThrowsWhenTenantIdMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:ClientId"] = "test-client-id",
                ["Office365:ClientSecret"] = "test-client-secret",
                ["Office365:SenderEmail"] = "sender@example.com"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new Office365EmailProvider(config, logger));
        Assert.Contains("TenantId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ThrowsWhenClientIdMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:TenantId"] = "test-tenant-id",
                ["Office365:ClientSecret"] = "test-client-secret",
                ["Office365:SenderEmail"] = "sender@example.com"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new Office365EmailProvider(config, logger));
        Assert.Contains("ClientId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ThrowsWhenClientSecretMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:TenantId"] = "test-tenant-id",
                ["Office365:ClientId"] = "test-client-id",
                ["Office365:SenderEmail"] = "sender@example.com"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new Office365EmailProvider(config, logger));
        Assert.Contains("ClientSecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ThrowsWhenSenderEmailMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:TenantId"] = "test-tenant-id",
                ["Office365:ClientId"] = "test-client-id",
                ["Office365:ClientSecret"] = "test-client-secret"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new Office365EmailProvider(config, logger));
        Assert.Contains("SenderEmail", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_SucceedsWithAllRequiredConfig()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:TenantId"] = "test-tenant-id",
                ["Office365:ClientId"] = "test-client-id",
                ["Office365:ClientSecret"] = "test-client-secret",
                ["Office365:SenderEmail"] = "sender@example.com"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();

        // Act & Assert (should not throw)
        using var provider = new Office365EmailProvider(config, logger);
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task SendEmailAsync_ReturnsFailureWhenNoRecipients()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Office365:TenantId"] = "test-tenant-id",
                ["Office365:ClientId"] = "test-client-id",
                ["Office365:ClientSecret"] = "test-client-secret",
                ["Office365:SenderEmail"] = "sender@example.com"
            })
            .Build();
        var logger = Substitute.For<ILogger<Office365EmailProvider>>();
        using var provider = new Office365EmailProvider(config, logger);

        // Act
        var result = await provider.SendEmailAsync(
            Array.Empty<string>(),
            "Test Subject",
            "<p>Test Body</p>");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No recipients", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailSendResult_RecordBehavior()
    {
        // Test record equality and properties
        var success = new EmailSendResult(true);
        var failure = new EmailSendResult(false, "Error message");

        Assert.True(success.Success);
        Assert.Null(success.ErrorMessage);
        Assert.False(failure.Success);
        Assert.Equal("Error message", failure.ErrorMessage);

        // Test record equality
        var successCopy = new EmailSendResult(true);
        Assert.Equal(success, successCopy);
    }
}
