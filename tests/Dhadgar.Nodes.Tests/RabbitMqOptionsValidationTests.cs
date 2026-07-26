using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Verifies that <see cref="RabbitMqOptions"/> rejects missing, empty, and whitespace-only
/// credentials through the same wiring Nodes uses at startup
/// (<c>AddOptions().Bind().ValidateDataAnnotations().ValidateOnStart()</c> in Program.cs).
/// The PR-cleared appsettings values are "" (not null), so [Required] must reject blanks
/// for the fail-fast to be real.
/// </summary>
public class RabbitMqOptionsValidationTests
{
    private static ServiceProvider BuildProvider(string? host, string? username, string? password)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = host,
            ["RabbitMq:Username"] = username,
            ["RabbitMq:Password"] = password
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();

        // Mirror the wiring in src/Dhadgar.Nodes/Program.cs
        services.AddOptions<RabbitMqOptions>()
            .Bind(config.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RabbitMqOptions_MissingOrBlankUsername_FailsValidation(string? username)
    {
        using var provider = BuildProvider("localhost", username, "valid-password");

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);

        Assert.Contains("Username", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RabbitMqOptions_MissingOrBlankPassword_FailsValidation(string? password)
    {
        using var provider = BuildProvider("localhost", "valid-user", password);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);

        Assert.Contains("Password", ex.Message);
    }

    [Fact]
    public void RabbitMqOptions_BlankCredentials_FailStartupValidation()
    {
        // ValidateOnStart is what turns validation failures into host-startup failures;
        // IStartupValidator is the hook the host invokes when it starts.
        using var provider = BuildProvider("localhost", "", "");

        var validator = provider.GetRequiredService<IStartupValidator>();

        Assert.Throws<OptionsValidationException>(() => validator.Validate());
    }

    [Fact]
    public void RabbitMqOptions_ValidCredentials_PassValidation()
    {
        using var provider = BuildProvider("rabbit.internal", "valid-user", "valid-password");

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Equal("rabbit.internal", options.Host);
        Assert.Equal("valid-user", options.Username);
        Assert.Equal("valid-password", options.Password);
        Assert.Equal("/", options.VirtualHost);
    }

    [Fact]
    public void RabbitMqOptions_ValidCredentials_PassStartupValidation()
    {
        using var provider = BuildProvider("localhost", "valid-user", "valid-password");

        var validator = provider.GetRequiredService<IStartupValidator>();

        validator.Validate();
    }
}
