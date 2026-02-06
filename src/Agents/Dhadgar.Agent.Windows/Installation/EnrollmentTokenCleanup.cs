using Dhadgar.Agent.Core.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Windows-specific implementation for cleaning up enrollment tokens from the registry.
/// </summary>
/// <remarks>
/// SECURITY: The enrollment token is stored at HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken
/// by the MSI installer when ENROLLMENT_TOKEN is provided during installation.
/// This value MUST be deleted after successful enrollment to prevent:
/// - Token reuse attacks
/// - Token leakage via registry inspection
/// - Exposure in system backups or registry exports
/// </remarks>
public sealed class EnrollmentTokenCleanup : IEnrollmentTokenCleanup
{
    /// <summary>
    /// Registry path where agent configuration is stored.
    /// </summary>
    private const string RegistryKeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>
    /// Registry value name for the enrollment token.
    /// </summary>
    private const string EnrollmentTokenValueName = "EnrollmentToken";

    private readonly ILogger<EnrollmentTokenCleanup> _logger;

    public EnrollmentTokenCleanup(ILogger<EnrollmentTokenCleanup> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void CleanupEnrollmentToken()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null)
            {
                _logger.LogDebug(
                    "Registry key {KeyPath} does not exist, no enrollment token to clean up",
                    RegistryKeyPath);
                return;
            }

            // Check if the value exists before attempting to delete
            var existingValue = key.GetValue(EnrollmentTokenValueName);
            if (existingValue is null)
            {
                _logger.LogDebug("No enrollment token found in registry, nothing to clean up");
                return;
            }

            // Delete the enrollment token value
            key.DeleteValue(EnrollmentTokenValueName, throwOnMissingValue: false);

            _logger.LogInformation(
                "Successfully removed enrollment token from registry at {KeyPath}\\{ValueName}",
                RegistryKeyPath,
                EnrollmentTokenValueName);
        }
        catch (UnauthorizedAccessException ex)
        {
            // SECURITY: Log but don't fail - the agent may not have write access to HKLM
            // This could happen if running under a restricted service account
            _logger.LogWarning(
                ex,
                "Insufficient permissions to remove enrollment token from registry. " +
                "Consider manually deleting HKLM\\{KeyPath}\\{ValueName} to prevent token exposure",
                RegistryKeyPath,
                EnrollmentTokenValueName);
        }
        catch (Exception ex)
        {
            // Log but don't fail - cleanup failure should not break enrollment
            _logger.LogWarning(
                ex,
                "Failed to remove enrollment token from registry. " +
                "Consider manually deleting HKLM\\{KeyPath}\\{ValueName} to prevent token exposure",
                RegistryKeyPath,
                EnrollmentTokenValueName);
        }
    }
}
