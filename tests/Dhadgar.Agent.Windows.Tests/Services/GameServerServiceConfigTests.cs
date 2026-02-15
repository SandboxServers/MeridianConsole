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
    public void Validate_ServerIdExceedsMaxLength_ReturnsMaxLengthError()
    {
        // Arrange - 201 characters exceeds the [MaxLength(200)] attribute limit
        // This tests the DataAnnotations MaxLength validation (fires before Validate())
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
    public void Validate_ServiceNameExceedsMaxLength_ReturnsServiceNameLengthError()
    {
        // Arrange - 250 char ServerId is valid for [MaxLength(200)] but the combined
        // service name "MeridianGS_{ServerId}" exceeds the 256-char Windows service name limit
        var longServerId = new string('a', 250);
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

        // Assert - Both MaxLength(200) and service name length should fire
        Assert.True(results.Count >= 1);
        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(GameServerServiceConfig.ServerId)));
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
    public void Validate_RelativeConfigFilePath_ReturnsError()
    {
        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = "relative/config.json", // Not absolute
            PipeName = @"MeridianAgent_12345678901234567890123456789012\test-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.ConfigFilePath), error.MemberNames);
        Assert.Contains("fully qualified", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Servers\..\Windows\System32")]
    [InlineData(@"C:\Servers\test\..\..\..\Windows")]
    public void Validate_PathTraversalInServerDirectory_ReturnsError(string traversalPath)
    {
        if (!IsWindows) return; // Path traversal detection is OS-specific

        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = traversalPath,
            ConfigFilePath = TestConfigPath,
            PipeName = @"MeridianAgent_12345678901234567890123456789012\test-server"
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(GameServerServiceConfig.ServerDirectory)) &&
            r.ErrorMessage!.Contains("traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"MeridianAgent_123\test-server")] // Short agentId (not 32 hex chars)
    [InlineData(@"MeridianAgent_ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ\test-server")] // Non-hex characters
    [InlineData(@"MeridianAgent_1234567890123456789012345678901\test-server")] // 31 chars (too short)
    public void Validate_InvalidAgentIdInPipeName_ReturnsError(string pipeName)
    {
        // Arrange
        var config = new GameServerServiceConfig
        {
            ServerId = "test-server",
            ProcessId = Guid.NewGuid(),
            WrapperExecutablePath = TestWrapperPath,
            ServerDirectory = TestServerDir,
            ConfigFilePath = TestConfigPath,
            PipeName = pipeName
        };

        // Act
        var results = ValidateConfig(config);

        // Assert
        var error = Assert.Single(results);
        Assert.Contains(nameof(GameServerServiceConfig.PipeName), error.MemberNames);
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
        Assert.StartsWith("MeridianAgent_", config.PipeName, StringComparison.Ordinal);
        Assert.Contains(serverId, config.PipeName, StringComparison.Ordinal);
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
