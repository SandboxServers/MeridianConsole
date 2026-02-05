using System.Runtime.InteropServices;

using Dhadgar.Agent.Windows.Security;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Security;

public sealed class DirectoryAclManagerTests
{
    // Platform-appropriate paths for testing
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string TestAbsolutePath = IsWindows
        ? @"C:\Servers\test"
        : "/var/servers/test";
    // Non-existent path MUST be within allowed roots (C:\Servers or /var/servers)
    private static readonly string TestNonExistentPath = IsWindows
        ? @"C:\Servers\nonexistent\path\that\does\not\exist"
        : "/var/servers/nonexistent/path/that/does/not/exist";
    private static readonly IReadOnlyList<string> TestAllowedRoots = IsWindows
        ? [@"C:\Servers", @"C:\GameServers"]
        : ["/var/servers", "/var/gameservers"];

    private readonly ILogger<DirectoryAclManager> _logger;
    private readonly DirectoryAclManager _aclManager;

    public DirectoryAclManagerTests()
    {
        _logger = Substitute.For<ILogger<DirectoryAclManager>>();
        _aclManager = new DirectoryAclManager(_logger, TestAllowedRoots);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new DirectoryAclManager(null!, TestAllowedRoots));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullAllowedRoots_ThrowsArgumentException()
    {
        // Act & Assert - Combined null/empty check throws ArgumentException
        var exception = Assert.Throws<ArgumentException>(
            () => new DirectoryAclManager(_logger, null!));
        Assert.Equal("allowedRoots", exception.ParamName);
        Assert.Contains("fail-closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithEmptyAllowedRoots_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => new DirectoryAclManager(_logger, []));
        Assert.Equal("allowedRoots", exception.ParamName);
        Assert.Contains("fail-closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithValidAllowedRoots_CreatesInstance()
    {
        // Act
        var manager = new DirectoryAclManager(_logger, TestAllowedRoots);

        // Assert
        Assert.NotNull(manager);
    }

    #endregion

    #region Path Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetupServerDirectory_EmptyPath_ReturnsFailure(string? path)
    {
        // Arrange
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.SetupServerDirectory(path!, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Directory path is required", result.Error);
    }

    [Fact]
    public void SetupServerDirectory_RelativePath_ReturnsFailure()
    {
        // Arrange
        var relativePath = "relative/path/to/server";
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.SetupServerDirectory(relativePath, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("fully qualified", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Servers\..\Windows\System32")]
    [InlineData(@"C:\Servers\test\..\..\Windows")]
    public void SetupServerDirectory_PathTraversal_ReturnsFailure(string pathWithTraversal)
    {
        // Only run on Windows (these paths are Windows-specific)
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.SetupServerDirectory(pathWithTraversal, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Service Account Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetupServerDirectory_EmptyServiceAccount_ReturnsFailure(string? serviceAccount)
    {
        // Arrange
        var path = TestAbsolutePath;

        // Act
        var result = _aclManager.SetupServerDirectory(path, serviceAccount!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Service account name is required", result.Error);
    }

    [Theory]
    [InlineData("LocalSystem")] // Wrong format
    [InlineData(@"NT SERVICE\InvalidService")] // Wrong prefix
    [InlineData(@"NT SERVICE\MeridianGS_")] // Missing ID
    [InlineData(@"DOMAIN\MeridianGS_test")] // Wrong domain
    public void SetupServerDirectory_InvalidServiceAccountFormat_ReturnsFailure(string serviceAccount)
    {
        // Arrange
        var path = TestAbsolutePath;

        // Act
        var result = _aclManager.SetupServerDirectory(path, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("NT SERVICE\\MeridianGS_", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"NT SERVICE\MeridianGS_test")]
    [InlineData(@"NT SERVICE\MeridianGS_test-server-123")]
    [InlineData(@"NT SERVICE\MeridianGS_test_server_456")]
    [InlineData(@"NT SERVICE\MeridianGS_a")]
    [Trait("Category", "Windows")]
    public void SetupServerDirectory_ValidServiceAccount_PassesValidation(string serviceAccount)
    {
        // Skip on non-Windows platforms (ACL APIs are Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Note: This test only verifies validation passes. The actual ACL operation
        // may fail due to permissions or the service not existing yet.

        // Arrange - Create temp directory first
        var tempPath = Path.Combine(Path.GetTempPath(), $"MeridianTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        // Create a custom ACL manager that includes the temp directory root in allowed roots
        // so we can reach service account validation instead of failing path validation
        var tempRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var allowedRootsWithTemp = new List<string>(TestAllowedRoots) { tempRoot };
        var aclManagerWithTempRoot = new DirectoryAclManager(_logger, allowedRootsWithTemp);

        try
        {
            // Act
            var result = aclManagerWithTempRoot.SetupServerDirectory(tempPath, serviceAccount);

            // Assert - Either succeeds or fails with ACL error (not validation error)
            if (result.IsFailure)
            {
                // Should not be a validation error about service account format
                Assert.DoesNotContain("Service account name is required", result.Error);
                Assert.DoesNotContain("NT SERVICE\\MeridianGS_", result.Error);
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, recursive: true); } catch { /* Ignore cleanup errors */ }
            }
        }
    }

    #endregion

    #region RemoveServerDirectoryAccess Tests

    [Fact]
    public void RemoveServerDirectoryAccess_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.RemoveServerDirectoryAccess(string.Empty, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Directory path is required", result.Error);
    }

    [Fact]
    public void RemoveServerDirectoryAccess_InvalidServiceAccount_ReturnsFailure()
    {
        // Arrange
        var path = TestAbsolutePath;
        var invalidAccount = "InvalidAccount";

        // Act
        var result = _aclManager.RemoveServerDirectoryAccess(path, invalidAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("NT SERVICE\\MeridianGS_", result.Error);
    }

    [Fact]
    public void RemoveServerDirectoryAccess_NonExistentDirectory_ReturnsSuccess()
    {
        // Arrange
        var nonExistentPath = TestNonExistentPath;
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.RemoveServerDirectoryAccess(nonExistentPath, serviceAccount);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void RemoveServerDirectoryAccess_ExistingDirectoryWithAcls_RemovesAccess()
    {
        // Skip on non-Windows platforms (ACL APIs are Windows-only)
        if (!IsWindows)
        {
            return;
        }

        // Arrange - Create temp directory and set up ACLs first
        var tempPath = Path.Combine(Path.GetTempPath(), $"MeridianTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        var tempRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var allowedRootsWithTemp = new List<string>(TestAllowedRoots) { tempRoot };
        var aclManagerWithTempRoot = new DirectoryAclManager(_logger, allowedRootsWithTemp);

        var serviceAccount = @"NT SERVICE\MeridianGS_test-cleanup";

        try
        {
            // Act - First setup, then remove
            // Setup may fail if service account doesn't exist yet, which is expected
            _ = aclManagerWithTempRoot.SetupServerDirectory(tempPath, serviceAccount);

            var removeResult = aclManagerWithTempRoot.RemoveServerDirectoryAccess(tempPath, serviceAccount);

            // Assert - Remove should succeed regardless of whether setup succeeded
            Assert.True(removeResult.IsSuccess);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, recursive: true); } catch { /* Ignore cleanup errors */ }
            }
        }
    }

    #endregion

    #region VerifyAccess Tests

    [Fact]
    public void VerifyAccess_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.VerifyAccess(string.Empty, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void VerifyAccess_InvalidServiceAccount_ReturnsFailure()
    {
        // Arrange
        var path = TestAbsolutePath;
        var invalidAccount = "Invalid";

        // Act
        var result = _aclManager.VerifyAccess(path, invalidAccount);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void VerifyAccess_NonExistentDirectory_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = TestNonExistentPath;
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.VerifyAccess(nonExistentPath, serviceAccount);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    #endregion

    #region DenyAccess Tests

    [Fact]
    public void DenyAccess_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.DenyAccess(string.Empty, serviceAccount);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Directory path is required", result.Error);
    }

    [Fact]
    public void DenyAccess_InvalidServiceAccount_ReturnsFailure()
    {
        // Arrange
        var path = TestAbsolutePath;
        var invalidAccount = "InvalidAccount";

        // Act
        var result = _aclManager.DenyAccess(path, invalidAccount);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void DenyAccess_NonExistentDirectory_ReturnsSuccess()
    {
        // Arrange - Directory doesn't exist, nothing to deny
        var nonExistentPath = TestNonExistentPath;
        var serviceAccount = @"NT SERVICE\MeridianGS_test";

        // Act
        var result = _aclManager.DenyAccess(nonExistentPath, serviceAccount);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion
}
