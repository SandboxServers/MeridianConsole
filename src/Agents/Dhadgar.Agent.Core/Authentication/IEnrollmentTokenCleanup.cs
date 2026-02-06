namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Platform-specific interface for cleaning up enrollment tokens after successful enrollment.
/// Windows implements this to delete the registry value, Linux may implement for file-based tokens.
/// </summary>
/// <remarks>
/// SECURITY: The enrollment token must be removed after successful enrollment to prevent:
/// - Token reuse attacks
/// - Token leakage from registry/file system inspection
/// - Exposure in system backups
/// </remarks>
public interface IEnrollmentTokenCleanup
{
    /// <summary>
    /// Removes the enrollment token from platform-specific storage.
    /// This should be called immediately after successful enrollment.
    /// </summary>
    /// <remarks>
    /// Implementations should:
    /// - Log warnings but not fail if cleanup cannot be performed
    /// - Handle missing tokens gracefully (token may not exist)
    /// - Not throw exceptions - cleanup failure should not break enrollment
    /// </remarks>
    void CleanupEnrollmentToken();
}
