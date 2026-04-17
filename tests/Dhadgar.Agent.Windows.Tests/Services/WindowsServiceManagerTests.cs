using System.Runtime.InteropServices;

using Dhadgar.Agent.Windows.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Services;

public sealed class WindowsServiceManagerTests
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

    private readonly ILogger<WindowsServiceManager> _logger;
    private readonly FakeTimeProvider _timeProvider;
    private readonly WindowsServiceManager _serviceManager;

    /// <summary>
    /// Fixed timestamp for deterministic testing.
    /// </summary>
    private static readonly DateTimeOffset FixedTimestamp = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public WindowsServiceManagerTests()
    {
        _logger = Substitute.For<ILogger<WindowsServiceManager>>();
        _timeProvider = new FakeTimeProvider(FixedTimestamp);
        _serviceManager = new WindowsServiceManager(_logger, _timeProvider);
    }

    #region ServerId Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateGameServerServiceAsync_EmptyServerId_ReturnsFailure(string? serverId)
    {
        // Arrange
        var config = CreateConfig(serverId ?? string.Empty);

        // Act
        var result = await _serviceManager.CreateGameServerServiceAsync(config);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Server ID is required", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test server")] // space
    [InlineData("test.server")] // dot
    [InlineData("test/server")] // slash
    [InlineData("test\\server")] // backslash
    [InlineData("test@server")] // at
    [InlineData("test;server")] // semicolon
    public async Task CreateGameServerServiceAsync_InvalidServerId_ReturnsFailure(string serverId)
    {
        // Arrange
        var config = CreateConfig(serverId);

        // Act
        var result = await _serviceManager.CreateGameServerServiceAsync(config);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateGameServerServiceAsync_ServerIdTooLong_ReturnsFailure()
    {
        // Arrange - 201 chars exceeds limit
        var longServerId = new string('a', 201);
        var config = CreateConfig(longServerId);

        // Act
        var result = await _serviceManager.CreateGameServerServiceAsync(config);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("maximum length", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("valid-server-id")]
    [InlineData("valid_server_id")]
    [InlineData("ValidServerId123")]
    [InlineData("a")]
    [InlineData("123")]
    [Trait("Category", "Windows")]
    public async Task CreateGameServerServiceAsync_ValidServerId_PassesValidation(string serverId)
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Note: This test only verifies validation passes, not actual service creation
        // (which would require admin privileges and would fail in CI)

        // Arrange
        var config = CreateConfig(serverId);

        // Act
        var result = await _serviceManager.CreateGameServerServiceAsync(config);

        // Assert
        // Will fail with "Wrapper not found" since the file doesn't exist,
        // but that means validation passed
        Assert.True(result.IsFailure);
        Assert.Contains("Wrapper executable not found", result.Error, StringComparison.Ordinal);
    }

    #endregion

    #region DeleteGameServerServiceAsync Tests

    [Fact]
    public async Task DeleteGameServerServiceAsync_InvalidServerId_ReturnsFailure()
    {
        // Arrange
        var invalidServerId = "invalid server!";

        // Act
        var result = await _serviceManager.DeleteGameServerServiceAsync(invalidServerId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task DeleteGameServerServiceAsync_NonExistentService_ReturnsSuccess()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange - Service that definitely doesn't exist
        var serverId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var result = await _serviceManager.DeleteGameServerServiceAsync(serverId);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region StartServiceAsync Tests

    [Fact]
    public async Task StartServiceAsync_InvalidServerId_ReturnsFailure()
    {
        // Arrange
        var invalidServerId = "invalid@server";

        // Act
        var result = await _serviceManager.StartServiceAsync(invalidServerId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task StartServiceAsync_NonExistentService_ReturnsFailure()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var serverId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var result = await _serviceManager.StartServiceAsync(serverId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("does not exist", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region StopServiceAsync Tests

    [Fact]
    public async Task StopServiceAsync_InvalidServerId_ReturnsFailure()
    {
        // Arrange
        var invalidServerId = "invalid$server";

        // Act
        var result = await _serviceManager.StopServiceAsync(
            invalidServerId,
            TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task StopServiceAsync_NonExistentService_ReturnsFailure()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var serverId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var result = await _serviceManager.StopServiceAsync(
            serverId,
            TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("does not exist", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region GetServiceStatusAsync Tests

    [Fact]
    public async Task GetServiceStatusAsync_InvalidServerId_ReturnsFailure()
    {
        // Arrange
        var invalidServerId = "invalid!server";

        // Act
        var result = await _serviceManager.GetServiceStatusAsync(invalidServerId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("invalid characters", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task GetServiceStatusAsync_NonExistentService_ReturnsNotInstalled()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var serverId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var result = await _serviceManager.GetServiceStatusAsync(serverId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceStatus.NotInstalled, result.Value);
    }

    #endregion

    #region ServiceExists Tests

    [Theory]
    [InlineData("invalid server")] // space
    [InlineData("invalid.server")] // dot
    [InlineData("invalid/server")] // slash
    public void ServiceExists_InvalidServerId_ReturnsFalse(string serverId)
    {
        // Act
        var exists = _serviceManager.ServiceExists(serverId);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void ServiceExists_NonExistentService_ReturnsFalse()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var serverId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var exists = _serviceManager.ServiceExists(serverId);

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region CleanupOrphanedServicesAsync Tests

    [Fact]
    [Trait("Category", "Windows")]
    public async Task CleanupOrphanedServicesAsync_EmptyActiveSet_ReturnsZero()
    {
        // Skip on non-Windows platforms (ServiceController is Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Note: This test verifies the method runs without error when there
        // are no game server services installed

        // Arrange
        var activeServerIds = new HashSet<string>();

        // Act
        var cleanedUp = await _serviceManager.CleanupOrphanedServicesAsync(activeServerIds);

        // Assert
        Assert.Equal(0, cleanedUp);
    }

    [Fact]
    public async Task CleanupOrphanedServicesAsync_NullActiveSet_ThrowsArgumentNull()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _serviceManager.CleanupOrphanedServicesAsync(null!));
    }

    #endregion

    #region Helpers

    private static GameServerServiceConfig CreateConfig(string serverId)
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

    #endregion
}
