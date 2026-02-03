using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

using Dhadgar.Agent.Windows.Services;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Services;

public sealed class GameServerServiceConfigTests
{
    // Platform-appropriate paths for testing
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string TestWrapperPath = IsWindows
        ? @"C:\Program Files\Agent\GameServerWrapper.exe"
        : "/opt/agent/GameServerWrapper";
    private static readonly string TestServerDir = IsWindows
        ? @"C:\Servers\test"
        : "/var/servers/test";
    private static readonly string TestConfigPath = IsWindows
        ? @"C:\Servers\test\config.json"
        : "/var/servers/test/config.json";

    [Fact]
    public void ServiceName_ReturnsCorrectFormat()
    {
        // Arrange
        var config = CreateValidConfig("test-server-123");

        // Act
        var serviceName = config.ServiceName;

        // Assert
        Assert.Equal("MeridianGS_test-server-123", serviceName);
    }

    [Fact]
    public void ServiceAccountName_ReturnsCorrectFormat()
    {
        // Arrange
        var config = CreateValidConfig("test-server-123");

        // Act
        var accountName = config.ServiceAccountName;

        // Assert
        Assert.Equal(@"NT SERVICE\MeridianGS_test-server-123", accountName);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("test-server")]
    [InlineData("test_server")]
    [InlineData("Test-Server-123")]
    [InlineData("a")]
    public void Validate_ValidServerId_ReturnsNoErrors(string serverId)
    {
        // Arrange
        var config = CreateValidConfig(serverId);

        // Act
        var results = ValidateConfig(config);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("test server")] // space
    [InlineData("test.server")] // dot
    [InlineData("test/server")] // slash
    [InlineData("test\\server")] // backslash
    [InlineData("test@server")] // at
    [InlineData("test$server")] // dollar
    [InlineData("test!server")] // exclamation
    public void Validate_InvalidServerId_ReturnsError(string serverId)
    {
        // Arrange - Use a fixed valid PipeName to isolate the ServerId validation
        var config = new GameServerServiceConfig
        {
            ServerId = serverId,
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = @"MeridianAgent_12345678901234567890123456789012\valid-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert - Check that exactly the ServerId error is present
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.ServerId), error.MemberNames);
        Assert.Contains("alphanumeric", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ServerIdTooLong_ReturnsError()
    {
        // Arrange - 201 characters exceeds the 200 char limit
        // Use a fixed valid PipeName to isolate the ServerId validation
        var longServerId = new string('a', 201);
        var config = new GameServerServiceConfig
        {
            ServerId = longServerId,
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = @"MeridianAgent_12345678901234567890123456789012\valid-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.ServerId), error.MemberNames);
        Assert.Contains("maximum length", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RelativeWrapperPath_ReturnsError()
    {
        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = "relative/path/wrapper.exe", // Not absolute
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = @"MeridianAgent_12345678901234567890123456789012\test-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.WrapperExecutablePath), error.MemberNames);
        Assert.Contains("fully qualified", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RelativeServerDirectory_ReturnsError()
    {
        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = "relative/servers/test", // Not absolute
            ConfigFilePath = TestConfigPath,
            PipeName = @"MeridianAgent_12345678901234567890123456789012\test-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.ServerDirectory), error.MemberNames);
        Assert.Contains("fully qualified", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_InvalidPipeNamePrefix_ReturnsError()
    {
        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = @"InvalidPrefix_12345678901234567890123456789012\test-server" // Wrong prefix
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.PipeName), error.MemberNames);
        Assert.Contains("MeridianAgent_", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ValidParameters_ReturnsConfig()
    {
        // Arrange
        var serverId = "test-server";
        var processId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Act
        var config = GameServerServiceConfig.Create(
            serverId, processId, TestWrapperPath, TestServerDir, TestConfigPath, agentId);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(serverId, config.ServerId);
        Assert.Equal(processId, config.ProcessId);
        Assert.Equal(TestWrapperPath, config.WrapperExecutablePath);
        Assert.Equal(TestServerDir, config.ServerDirectory);
        Assert.Equal(TestConfigPath, config.ConfigFilePath);
        Assert.StartsWith("MeridianAgent_", config.PipeName);
        Assert.Contains(serverId, config.PipeName);
    }

    [Fact]
    public void Create_InvalidServerId_ReturnsNull()
    {
        // Arrange
        var serverId = "invalid server!"; // Invalid chars
        var agentId = Guid.NewGuid();

        // Act
        var config = GameServerServiceConfig.Create(
            serverId,
            Guid.NewGuid(),
            TestWrapperPath,
            TestServerDir,
            TestConfigPath,
            agentId);

        // Assert
        Assert.Null(config);
    }

    private static GameServerServiceConfig CreateValidConfig(string serverId)
    {
        return new GameServerServiceConfig
        {
            ServerId = serverId,
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = $@"MeridianAgent_12345678901234567890123456789012\{serverId}"
        };
    }

    private static List<ValidationResult> ValidateConfig(GameServerServiceConfig config)
    {
        var context = new ValidationContext(config);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, context, results, validateAllProperties: true);
        return results;
    }
}
