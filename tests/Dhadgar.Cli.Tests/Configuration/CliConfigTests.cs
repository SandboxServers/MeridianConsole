using System.Text.Json;
using Dhadgar.Cli.Configuration;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Configuration;

[Collection("CliConfig")]
public class CliConfigTests
{
    [Fact]
    public void IsAuthenticated_WithValidToken_ReturnsTrue()
    {
        var config = new CliConfig
        {
            AccessToken = "valid-token",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        config.IsAuthenticated().Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_WithExpiredToken_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = "valid-token",
            TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };

        config.IsAuthenticated().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithTokenExpiringWithinOneMinute_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = "valid-token",
            TokenExpiresAt = DateTime.UtcNow.AddSeconds(30)
        };

        config.IsAuthenticated().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithNullAccessToken_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = null,
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        config.IsAuthenticated().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithEmptyAccessToken_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = "   ",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        config.IsAuthenticated().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WithNullExpiration_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = "valid-token",
            TokenExpiresAt = null
        };

        config.IsAuthenticated().Should().BeFalse();
    }

    [Fact]
    public void EffectiveIdentityUrl_WithExplicitUrl_ReturnsExplicitUrl()
    {
        var config = new CliConfig
        {
            IdentityUrl = "http://custom-identity:5001",
            GatewayUrl = "http://gateway:5000"
        };

        config.EffectiveIdentityUrl.Should().Be("http://custom-identity:5001");
    }

    [Fact]
    public void EffectiveIdentityUrl_WithoutExplicitUrl_ReturnsGatewayBasedUrl()
    {
        var config = new CliConfig
        {
            IdentityUrl = null,
            GatewayUrl = "http://my-gateway:8080"
        };

        config.EffectiveIdentityUrl.Should().Be("http://my-gateway:8080/api/v1/identity");
    }

    [Fact]
    public void EffectiveNodesUrl_WithoutExplicitUrl_ReturnsGatewayApiV1Url()
    {
        var config = new CliConfig
        {
            NodesUrl = null,
            GatewayUrl = "http://my-gateway:8080/"
        };

        config.EffectiveNodesUrl.Should().Be("http://my-gateway:8080/api/v1");
    }

    [Fact]
    public void EffectiveIdentityUrl_WithTrailingSlashOnGateway_RemovesTrailingSlash()
    {
        var config = new CliConfig
        {
            IdentityUrl = null,
            GatewayUrl = "http://my-gateway:8080/"
        };

        config.EffectiveIdentityUrl.Should().Be("http://my-gateway:8080/api/v1/identity");
    }

    [Fact]
    public void EffectiveIdentityUrl_WithNullGateway_UsesLocalhost()
    {
        var config = new CliConfig
        {
            IdentityUrl = null,
            GatewayUrl = null
        };

        config.EffectiveIdentityUrl.Should().Be("http://localhost:5000/api/v1/identity");
    }

    [Fact]
    public void Load_FirstRun_WritesGatewayUrlOnly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dhadgar-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        CliConfig.ConfigDirectoryOverride = directory;

        try
        {
            var config = CliConfig.Load();

            config.GatewayUrl.Should().Be("http://localhost:5000");
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
            document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("gateway_url");
        }
        finally
        {
            CliConfig.ConfigDirectoryOverride = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EffectiveSecretsUrl_WithExplicitUrl_ReturnsExplicitUrl()
    {
        var config = new CliConfig
        {
            SecretsUrl = "http://custom-secrets:5002",
            GatewayUrl = "http://gateway:5000"
        };

        config.EffectiveSecretsUrl.Should().Be("http://custom-secrets:5002");
    }

    [Fact]
    public void EffectiveSecretsUrl_WithoutExplicitUrl_ReturnsGatewayBasedUrl()
    {
        var config = new CliConfig
        {
            SecretsUrl = null,
            GatewayUrl = "http://my-gateway:8080"
        };

        config.EffectiveSecretsUrl.Should().Be("http://my-gateway:8080/secrets");
    }

    [Fact]
    public void EffectiveGatewayUrl_WithExplicitUrl_ReturnsUrlWithoutTrailingSlash()
    {
        var config = new CliConfig
        {
            GatewayUrl = "http://my-gateway:8080/"
        };

        config.EffectiveGatewayUrl.Should().Be("http://my-gateway:8080");
    }

    [Fact]
    public void EffectiveGatewayUrl_WithNullUrl_ReturnsLocalhost()
    {
        var config = new CliConfig
        {
            GatewayUrl = null
        };

        config.EffectiveGatewayUrl.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void NewConfig_HasNullTokens()
    {
        var config = new CliConfig();

        config.AccessToken.Should().BeNull();
        config.RefreshToken.Should().BeNull();
        config.TokenExpiresAt.Should().BeNull();
        config.CurrentOrgId.Should().BeNull();
    }
}
