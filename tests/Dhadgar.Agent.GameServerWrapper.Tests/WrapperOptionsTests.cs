using System.Runtime.InteropServices;

using Dhadgar.Agent.GameServerWrapper;

using Xunit;

namespace Dhadgar.Agent.GameServerWrapper.Tests;

public sealed class WrapperOptionsTests
{
    // Platform-appropriate paths for testing
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string TestConfigPath = IsWindows
        ? @"C:\config.json"
        : "/etc/config.json";
    private static readonly string NonExistentConfigPath = IsWindows
        ? @"C:\NonExistent\Path\config.json"
        : "/nonexistent/path/config.json";
    #region Parse Tests

    [Fact]
    public void Parse_ValidArguments_ReturnsOptions()
    {
        // Arrange - Create a temp config file to pass validation
        var tempConfig = Path.GetTempFileName();
        try
        {
            var args = new[]
            {
                "--server-id=test-server",
                "--pipe=MeridianAgent_12345\\test-server",
                $"--config={tempConfig}"
            };

            // Act
            var result = WrapperOptions.Parse(args);

            // Assert
            Assert.True(result.IsSuccess);
            var options = result.Value;
            Assert.Equal("test-server", options.ServerId);
            Assert.Equal("MeridianAgent_12345\\test-server", options.PipeName);
            Assert.Equal(tempConfig, options.ConfigPath);
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    [Fact]
    public void Parse_ValidArgumentsWithSpaces_ReturnsOptions()
    {
        // Arrange - Create a temp config file to pass validation
        var tempConfig = Path.GetTempFileName();
        try
        {
            var args = new[]
            {
                "--server-id", "test-server",
                "--pipe", "MeridianAgent_12345\\test-server",
                "--config", tempConfig
            };

            // Act
            var result = WrapperOptions.Parse(args);

            // Assert
            Assert.True(result.IsSuccess);
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    [Fact]
    public void Parse_MissingServerId_ReturnsError()
    {
        // Arrange
        var args = new[]
        {
            "--pipe=MeridianAgent_12345\\test",
            "--config=C:\\config.json"
        };

        // Act
        var result = WrapperOptions.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("--server-id", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingPipe_ReturnsError()
    {
        // Arrange
        var args = new[]
        {
            "--server-id=test",
            "--config=C:\\config.json"
        };

        // Act
        var result = WrapperOptions.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("--pipe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingConfig_ReturnsError()
    {
        // Arrange
        var args = new[]
        {
            "--server-id=test",
            "--pipe=MeridianAgent_12345\\test"
        };

        // Act
        var result = WrapperOptions.Parse(args);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("--config", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_QuotedConfigPath_ParsesCorrectly()
    {
        // Arrange - Create a temp config file to pass validation
        var tempConfig = Path.GetTempFileName();
        try
        {
            var args = new[]
            {
                "--server-id=test",
                "--pipe=MeridianAgent_12345\\test",
                $"--config=\"{tempConfig}\""
            };

            // Act
            var result = WrapperOptions.Parse(args);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(tempConfig, result.Value.ConfigPath);
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_ValidOptions_ReturnsEmptyErrors()
    {
        // Arrange
        var tempConfig = Path.GetTempFileName();
        try
        {
            var options = new WrapperOptions
            {
                ServerId = "test-server",
                PipeName = "MeridianAgent_12345\\test-server",
                ConfigPath = tempConfig
            };

            // Act
            var errors = options.Validate();

            // Assert
            Assert.Empty(errors);
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyServerId_ReturnsError(string serverId)
    {
        // Arrange
        var options = new WrapperOptions
        {
            ServerId = serverId,
            PipeName = "MeridianAgent_12345\\test",
            ConfigPath = TestConfigPath
        };

        // Act
        var errors = options.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("server-id", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("test server")] // space
    [InlineData("test.server")] // dot
    [InlineData("test/server")] // slash
    [InlineData("test\\server")] // backslash
    [InlineData("test@server")] // at
    public void Validate_InvalidServerId_ReturnsError(string serverId)
    {
        // Arrange
        var options = new WrapperOptions
        {
            ServerId = serverId,
            PipeName = "MeridianAgent_12345\\test",
            ConfigPath = TestConfigPath
        };

        // Act
        var errors = options.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("invalid characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_InvalidPipePrefix_ReturnsError()
    {
        // Arrange
        var tempConfig = Path.GetTempFileName();
        try
        {
            var options = new WrapperOptions
            {
                ServerId = "test-server",
                PipeName = "WrongPrefix_12345\\test", // Should start with MeridianAgent_
                ConfigPath = tempConfig
            };

            // Act
            var errors = options.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("MeridianAgent_", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    [Fact]
    public void Validate_RelativeConfigPath_ReturnsError()
    {
        // Arrange
        var options = new WrapperOptions
        {
            ServerId = "test-server",
            PipeName = "MeridianAgent_12345\\test",
            ConfigPath = "relative/path/config.json"
        };

        // Act
        var errors = options.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("absolute path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NonExistentConfigPath_ReturnsError()
    {
        // Arrange
        var options = new WrapperOptions
        {
            ServerId = "test-server",
            PipeName = "MeridianAgent_12345\\test",
            ConfigPath = NonExistentConfigPath
        };

        // Act
        var errors = options.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
