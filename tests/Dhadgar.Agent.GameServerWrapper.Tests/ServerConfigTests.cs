using System.Runtime.InteropServices;

using Dhadgar.Agent.GameServerWrapper;

using Xunit;

namespace Dhadgar.Agent.GameServerWrapper.Tests;

public sealed class ServerConfigTests
{
    // Platform-appropriate paths for testing
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string NonExistentPath = IsWindows
        ? @"C:\NonExistent\Path\server.exe"
        : "/nonexistent/path/server.exe";
    private static readonly string NonExistentDirectory = IsWindows
        ? @"C:\NonExistent\Path"
        : "/nonexistent/path";
    private static readonly string NonExistentConfigPath = IsWindows
        ? @"C:\NonExistent\config.json"
        : "/nonexistent/config.json";
    #region Validation Tests

    [Fact]
    public void Validate_ValidConfig_ReturnsEmptyErrors()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        var tempDir = Path.GetDirectoryName(tempExe)!;

        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                WorkingDirectory = tempDir,
                Arguments = "-port 27015",
                CaptureStdout = true,
                CaptureStderr = true,
                RedirectStdin = true,
                AutoRestart = false,
                MaxRestartAttempts = 3,
                RestartDelaySeconds = 5,
                GracefulShutdownTimeoutSeconds = 30
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Empty(errors);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyExecutablePath_ReturnsError(string? executablePath)
    {
        // Arrange
        var config = new ServerConfig
        {
            ExecutablePath = executablePath!
        };

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("ExecutablePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RelativeExecutablePath_ReturnsError()
    {
        // Arrange
        var config = new ServerConfig
        {
            ExecutablePath = "relative/path/server.exe"
        };

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("absolute path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NonExistentExecutable_ReturnsError()
    {
        // Arrange
        var config = new ServerConfig
        {
            ExecutablePath = NonExistentPath
        };

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RelativeWorkingDirectory_ReturnsError()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                WorkingDirectory = "relative/path"
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("WorkingDirectory", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public void Validate_NonExistentWorkingDirectory_ReturnsError()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                WorkingDirectory = NonExistentDirectory
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("Working directory not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_InvalidRestartDelaySeconds_ReturnsError(int restartDelay)
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                RestartDelaySeconds = restartDelay
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("RestartDelaySeconds", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(200)]
    public void Validate_InvalidCpuLimitPercent_ReturnsError(int cpuLimit)
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                CpuLimitPercent = cpuLimit
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("CpuLimitPercent", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public void Validate_NegativeMemoryLimit_ReturnsError()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                MemoryLimitMb = -100
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("MemoryLimitMb", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Fact]
    public void Validate_NegativeMaxRestartAttempts_ReturnsError()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                MaxRestartAttempts = -1
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("MaxRestartAttempts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidGracefulShutdownTimeout_ReturnsError(int timeout)
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                GracefulShutdownTimeoutSeconds = timeout
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.Contains(errors, e => e.Contains("GracefulShutdownTimeoutSeconds", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    #endregion

    #region LoadFromFile Tests

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsError()
    {
        // Arrange & Act
        var result = ServerConfig.LoadFromFile(NonExistentConfigPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromFile_InvalidJson_ReturnsError()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{ invalid json }");

            // Act
            var result = ServerConfig.LoadFromFile(tempFile);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid JSON", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_ValidJson_ReturnsConfig()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        var tempConfig = Path.GetTempFileName();
        try
        {
            var json = $$"""
                {
                    "executablePath": "{{tempExe.Replace("\\", "\\\\")}}",
                    "arguments": "-port 27015",
                    "captureStdout": true,
                    "captureStderr": false,
                    "autoRestart": true,
                    "maxRestartAttempts": 5,
                    "restartDelaySeconds": 10,
                    "cpuLimitPercent": 50,
                    "memoryLimitMb": 1024,
                    "gracefulShutdownTimeoutSeconds": 60
                }
                """;
            File.WriteAllText(tempConfig, json);

            // Act
            var result = ServerConfig.LoadFromFile(tempConfig);

            // Assert
            Assert.True(result.IsSuccess);
            var config = result.Value;
            Assert.Equal(tempExe, config.ExecutablePath);
            Assert.Equal("-port 27015", config.Arguments);
            Assert.True(config.CaptureStdout);
            Assert.False(config.CaptureStderr);
            Assert.True(config.AutoRestart);
            Assert.Equal(5, config.MaxRestartAttempts);
            Assert.Equal(10, config.RestartDelaySeconds);
            Assert.Equal(50, config.CpuLimitPercent);
            Assert.Equal(1024, config.MemoryLimitMb);
            Assert.Equal(60, config.GracefulShutdownTimeoutSeconds);
        }
        finally
        {
            File.Delete(tempExe);
            File.Delete(tempConfig);
        }
    }

    [Fact]
    public void LoadFromFile_JsonWithNonExistentExecutable_ReturnsError()
    {
        // Arrange
        var tempConfig = Path.GetTempFileName();
        try
        {
            // Use platform-appropriate path in JSON
            var escapedPath = NonExistentPath.Replace("\\", "\\\\", StringComparison.Ordinal);
            var json = $$"""
                {
                    "executablePath": "{{escapedPath}}"
                }
                """;
            File.WriteAllText(tempConfig, json);

            // Act
            var result = ServerConfig.LoadFromFile(tempConfig);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempConfig);
        }
    }

    #endregion

    #region SaveToFile Tests

    [Fact]
    public void SaveToFile_ValidConfig_WritesJson()
    {
        // Arrange
        var tempExe = Path.GetTempFileName();
        var tempConfig = Path.GetTempFileName();
        try
        {
            var config = new ServerConfig
            {
                ExecutablePath = tempExe,
                Arguments = "-test",
                CpuLimitPercent = 75
            };

            // Act
            config.SaveToFile(tempConfig);
            var json = File.ReadAllText(tempConfig);

            // Assert
            Assert.Contains("executablePath", json);
            Assert.Contains(tempExe.Replace("\\", "\\\\"), json);
            Assert.Contains("-test", json);
            Assert.Contains("75", json);
        }
        finally
        {
            File.Delete(tempExe);
            File.Delete(tempConfig);
        }
    }

    #endregion
}
