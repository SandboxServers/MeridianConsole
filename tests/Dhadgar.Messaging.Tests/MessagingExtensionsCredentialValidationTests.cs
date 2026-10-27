using Dhadgar.Messaging.Publishing;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Messaging.Tests;

/// <summary>
/// Verifies that <see cref="MessagingExtensions.AddDhadgarMessaging"/> fails fast when
/// RabbitMQ credentials are missing, empty, or whitespace-only. A plain null-coalescing
/// check is not sufficient because cleared appsettings values are "" (not null).
/// Validation runs at bus creation, which is what host startup does — the tests trigger
/// it by resolving <see cref="IBusControl"/> (the bus is created but never started, so
/// no broker connection is attempted). Providers are disposed asynchronously, as the host
/// does, because MassTransit registers services that only implement IAsyncDisposable.
/// </summary>
public class MessagingExtensionsCredentialValidationTests
{
    private static IConfiguration BuildConfiguration(string? username, string? password)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Username"] = username,
            ["RabbitMq:Password"] = password
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddDhadgarMessaging(config);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddDhadgarMessaging_MissingOrBlankUsername_ThrowsNamingConfigKey(string? username)
    {
        await using var provider = BuildProvider(BuildConfiguration(username, "valid-password"));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBusControl>());

        Assert.Contains("RabbitMq:Username", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddDhadgarMessaging_MissingOrBlankPassword_ThrowsNamingConfigKey(string? password)
    {
        await using var provider = BuildProvider(BuildConfiguration("valid-user", password));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBusControl>());

        Assert.Contains("RabbitMq:Password", ex.Message);
    }

    [Fact]
    public async Task AddDhadgarMessaging_MissingRabbitMqSection_ThrowsNamingConfigKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        await using var provider = BuildProvider(config);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBusControl>());

        Assert.Contains("RabbitMq:Username", ex.Message);
    }

    [Fact]
    public async Task AddDhadgarMessaging_ValidCredentials_BusCreationSucceeds()
    {
        await using var provider = BuildProvider(BuildConfiguration("valid-user", "valid-password"));

        var bus = provider.GetRequiredService<IBusControl>();

        Assert.NotNull(bus);
    }

    [Fact]
    public void AddDhadgarMessaging_RegistersEventPublisher()
    {
        var services = new ServiceCollection();

        var result = services.AddDhadgarMessaging(BuildConfiguration("valid-user", "valid-password"));

        Assert.Same(services, result);
        Assert.Contains(services, d => d.ServiceType == typeof(IEventPublisher));
    }
}
