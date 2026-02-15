using Dhadgar.Agent.Core.Process;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

public sealed class WindowsProcessManagerTests
{
    private readonly ILogger<WindowsProcessManager> _logger;
    private readonly FakeTimeProvider _timeProvider;

    public WindowsProcessManagerTests()
    {
        _logger = Substitute.For<ILogger<WindowsProcessManager>>();
        _timeProvider = new FakeTimeProvider();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new WindowsProcessManager(null!, _timeProvider));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_DefaultsToSystemTimeProvider()
    {
        // Act
        using var manager = new WindowsProcessManager(_logger, timeProvider: null);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Assert
        Assert.NotNull(manager);
    }

    #endregion

    #region StartProcessAsync Tests

    [Fact]
    public async Task StartProcessAsync_WhenDisposed_ReturnsFailure()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        var config = CreateValidProcessConfig();

        // Act
        var result = await manager.StartProcessAsync(config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.Disposed]", result.Error, StringComparison.Ordinal);
        Assert.Contains("disposed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartProcessAsync_WithNullConfig_ThrowsArgumentNullException()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await manager.StartProcessAsync(null!));
    }

    [Fact]
    public async Task StartProcessAsync_WithInvalidExecutablePath_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        var config = new ProcessConfig
        {
            ServerId = Guid.NewGuid(),
            ExecutablePath = string.Empty // Invalid: empty path
        };

        // Act
        var result = await manager.StartProcessAsync(config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.InvalidPath]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartProcessAsync_WithNonExistentExecutable_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Use a path that is absolute and fully-qualified on the current platform
        // but doesn't exist
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N"), "executable.exe");

        var config = new ProcessConfig
        {
            ServerId = Guid.NewGuid(),
            ExecutablePath = nonExistentPath
        };

        // Act
        var result = await manager.StartProcessAsync(config);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.NotFound]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartProcessAsync_WithInvalidWorkingDirectory_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Create a temporary executable for testing
        var tempExe = CreateTemporaryExecutable();

        try
        {
            // Use a path that is absolute and fully-qualified on the current platform
            // but doesn't exist
            var nonExistentWorkDir = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N"));

            var config = new ProcessConfig
            {
                ServerId = Guid.NewGuid(),
                ExecutablePath = tempExe,
                WorkingDirectory = nonExistentWorkDir
            };

            // Act
            var result = await manager.StartProcessAsync(config);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("[Process.WorkingDirNotFound]", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            CleanupTemporaryFile(tempExe);
        }
    }

    [Fact]
    public async Task StartProcessAsync_WithInvalidExtension_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Create a temp file with invalid extension
        var tempFile = Path.GetTempFileName();
        var invalidFile = Path.ChangeExtension(tempFile, ".txt");
        File.Move(tempFile, invalidFile);

        try
        {
            var config = new ProcessConfig
            {
                ServerId = Guid.NewGuid(),
                ExecutablePath = invalidFile
            };

            // Act
            var result = await manager.StartProcessAsync(config);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("[Process.InvalidExtension]", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            CleanupTemporaryFile(invalidFile);
        }
    }

    #endregion

    #region StopProcessAsync Tests

    [Fact]
    public async Task StopProcessAsync_WhenDisposed_ReturnsFailure()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        var processId = Guid.NewGuid();

        // Act
        var result = await manager.StopProcessAsync(processId, TimeSpan.FromSeconds(5));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.Disposed]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopProcessAsync_WithUnknownProcessId_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);
        var unknownProcessId = Guid.NewGuid();

        // Act
        var result = await manager.StopProcessAsync(unknownProcessId, TimeSpan.FromSeconds(5));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.NotFound]", result.Error, StringComparison.Ordinal);
        Assert.Contains(unknownProcessId.ToString(), result.Error, StringComparison.Ordinal);
    }

    #endregion

    #region KillProcessAsync Tests

    [Fact]
    public async Task KillProcessAsync_WhenDisposed_ReturnsFailure()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        var processId = Guid.NewGuid();

        // Act
        var result = await manager.KillProcessAsync(processId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.Disposed]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KillProcessAsync_WithUnknownProcessId_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);
        var unknownProcessId = Guid.NewGuid();

        // Act
        var result = await manager.KillProcessAsync(unknownProcessId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.NotFound]", result.Error, StringComparison.Ordinal);
        Assert.Contains(unknownProcessId.ToString(), result.Error, StringComparison.Ordinal);
    }

    #endregion

    #region GetProcess Tests

    [Fact]
    public void GetProcess_WhenDisposed_ReturnsNull()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        var processId = Guid.NewGuid();

        // Act
        var result = manager.GetProcess(processId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetProcess_WithUnknownProcessId_ReturnsNull()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);
        var unknownProcessId = Guid.NewGuid();

        // Act
        var result = manager.GetProcess(unknownProcessId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAllProcesses Tests

    [Fact]
    public void GetAllProcesses_WhenDisposed_ReturnsEmptyList()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        // Act
        var result = manager.GetAllProcesses();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllProcesses_WhenNoProcesses_ReturnsEmptyList()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Act
        var result = manager.GetAllProcesses();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region UpdateResourceLimitsAsync Tests

    [Fact]
    public async Task UpdateResourceLimitsAsync_WhenDisposed_ReturnsFailure()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        manager.Dispose();

        var processId = Guid.NewGuid();
        var limits = new ResourceLimits { CpuPercent = 50, MemoryMb = 512 };

        // Act
        var result = await manager.UpdateResourceLimitsAsync(processId, limits);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.Disposed]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateResourceLimitsAsync_WithUnknownProcessId_ReturnsFailure()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);
        var unknownProcessId = Guid.NewGuid();
        var limits = new ResourceLimits { CpuPercent = 50, MemoryMb = 512 };

        // Act
        var result = await manager.UpdateResourceLimitsAsync(unknownProcessId, limits);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("[Process.NotFound]", result.Error, StringComparison.Ordinal);
        Assert.Contains(unknownProcessId.ToString(), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateResourceLimitsAsync_WithNullLimits_ThrowsArgumentNullException()
    {
        // Arrange
        using var manager = new WindowsProcessManager(_logger, _timeProvider);
        var processId = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await manager.UpdateResourceLimitsAsync(processId, null!));
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);

        // Act & Assert
        manager.Dispose();
        manager.Dispose(); // Second dispose should not throw
        manager.Dispose(); // Third dispose should not throw
    }

    [Fact]
    public async Task Dispose_MakesSubsequentOperationsReturnFailure()
    {
        // Arrange
        var manager = new WindowsProcessManager(_logger, _timeProvider);
        var config = CreateValidProcessConfig();
        var processId = Guid.NewGuid();
        var limits = new ResourceLimits { CpuPercent = 50 };

        // Act
        manager.Dispose();

        // Assert
        var startResult = await manager.StartProcessAsync(config);
        Assert.False(startResult.IsSuccess);
        Assert.Contains("[Process.Disposed]", startResult.Error, StringComparison.Ordinal);

        var stopResult = await manager.StopProcessAsync(processId, TimeSpan.FromSeconds(5));
        Assert.False(stopResult.IsSuccess);
        Assert.Contains("[Process.Disposed]", stopResult.Error, StringComparison.Ordinal);

        var killResult = await manager.KillProcessAsync(processId);
        Assert.False(killResult.IsSuccess);
        Assert.Contains("[Process.Disposed]", killResult.Error, StringComparison.Ordinal);

        var updateResult = await manager.UpdateResourceLimitsAsync(processId, limits);
        Assert.False(updateResult.IsSuccess);
        Assert.Contains("[Process.Disposed]", updateResult.Error, StringComparison.Ordinal);

        var getResult = manager.GetProcess(processId);
        Assert.Null(getResult);

        var getAllResult = manager.GetAllProcesses();
        Assert.Empty(getAllResult);
    }

    #endregion

    #region Helper Methods

    private static ProcessConfig CreateValidProcessConfig()
    {
        // Use a cross-platform path for testing
        // Note: This is for creating config only, the executable may not actually exist
        // on all platforms - tests using this config should handle that appropriately
        var execPath = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32\notepad.exe"
            : "/bin/echo";

        return new ProcessConfig
        {
            ServerId = Guid.NewGuid(),
            ExecutablePath = execPath,
            CaptureStdout = false,
            CaptureStderr = false
        };
    }

    private static string CreateTemporaryExecutable()
    {
        var tempFile = Path.GetTempFileName();
        var exeFile = Path.ChangeExtension(tempFile, ".exe");
        File.Move(tempFile, exeFile);
        return exeFile;
    }

    private static void CleanupTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    #endregion
}
