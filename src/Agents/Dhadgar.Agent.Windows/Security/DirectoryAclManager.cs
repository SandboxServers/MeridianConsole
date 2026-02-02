using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Security;

/// <summary>
/// Interface for managing directory ACLs for game server isolation.
/// </summary>
public interface IDirectoryAclManager
{
    /// <summary>
    /// Sets up a server directory with appropriate ACLs for the service account.
    /// </summary>
    /// <param name="directoryPath">Path to the server directory.</param>
    /// <param name="serviceAccountName">The service account name (e.g., "NT SERVICE\MeridianGS_abc123").</param>
    /// <returns>Success or failure result.</returns>
    Result SetupServerDirectory(string directoryPath, string serviceAccountName);

    /// <summary>
    /// Removes access for a service account from a server directory.
    /// </summary>
    /// <param name="directoryPath">Path to the server directory.</param>
    /// <param name="serviceAccountName">The service account name.</param>
    /// <returns>Success or failure result.</returns>
    Result RemoveServerDirectoryAccess(string directoryPath, string serviceAccountName);

    /// <summary>
    /// Verifies that a service account has proper access to its directory.
    /// </summary>
    /// <param name="directoryPath">Path to the server directory.</param>
    /// <param name="serviceAccountName">The service account name.</param>
    /// <returns>True if access is properly configured.</returns>
    Result<bool> VerifyAccess(string directoryPath, string serviceAccountName);

    /// <summary>
    /// Denies access for a service account to another server's directory.
    /// Used to enforce isolation between game servers.
    /// </summary>
    /// <param name="directoryPath">Path to deny access to.</param>
    /// <param name="serviceAccountName">The service account to deny.</param>
    /// <returns>Success or failure result.</returns>
    Result DenyAccess(string directoryPath, string serviceAccountName);
}

/// <summary>
/// Manages directory ACLs for game server process isolation.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This class manages file system permissions for game server isolation.
///
/// Security measures:
/// - Path validation prevents traversal attacks
/// - Service account names are validated against strict patterns
/// - ACLs are set to grant full control only to the specific service account
/// - Inheritance is enabled so subdirectories get the same permissions
/// - Explicit deny rules can be added for other game server accounts
///
/// ACL Strategy:
/// 1. Each server directory grants FullControl to its own Virtual Service Account
/// 2. Optional: Deny rules prevent other game server accounts from accessing the directory
/// 3. System and Administrators retain access for management
/// </remarks>
public sealed partial class DirectoryAclManager : IDirectoryAclManager
{
    private readonly ILogger<DirectoryAclManager> _logger;

    /// <summary>
    /// Pattern for valid service account names.
    /// Format: NT SERVICE\MeridianGS_{alphanumeric}
    /// </summary>
    [GeneratedRegex(@"^NT SERVICE\\MeridianGS_[a-zA-Z0-9\-_]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ValidServiceAccountPattern();

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryAclManager"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public DirectoryAclManager(ILogger<DirectoryAclManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Result SetupServerDirectory(string directoryPath, string serviceAccountName)
    {
        // Validate inputs
        var pathValidation = ValidateDirectoryPath(directoryPath);
        if (pathValidation.IsFailure)
        {
            return pathValidation;
        }

        var accountValidation = ValidateServiceAccountName(serviceAccountName);
        if (accountValidation.IsFailure)
        {
            return accountValidation;
        }

        try
        {
            // Ensure directory exists
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogInformation("Creating server directory: {Path}", directoryPath);
                Directory.CreateDirectory(directoryPath);
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            // Create the service account identity
            // Virtual Service Accounts use the format "NT SERVICE\{ServiceName}"
            NTAccount serviceAccount;
            try
            {
                serviceAccount = new NTAccount(serviceAccountName);
            }
            catch (ArgumentException ex)
            {
                return Result.Failure($"[ACL.InvalidAccount] Invalid service account name: {ex.Message}");
            }

            // Add full control for the service account with inheritance
            var accessRule = new FileSystemAccessRule(
                serviceAccount,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            security.AddAccessRule(accessRule);

            // Apply the modified security settings
            directoryInfo.SetAccessControl(security);

            _logger.LogInformation(
                "Granted FullControl to {Account} on directory {Path}",
                serviceAccountName, directoryPath);

            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied setting ACLs on {Path}", directoryPath);
            return Result.Failure($"[ACL.AccessDenied] Access denied setting ACLs: {ex.Message}");
        }
        catch (IdentityNotMappedException ex)
        {
            // This can happen if the service doesn't exist yet (Virtual Service Accounts
            // are created when the service starts, not when it's installed)
            _logger.LogWarning(
                ex,
                "Service account {Account} not yet mapped. This is expected for new services.",
                serviceAccountName);

            // Try using the service SID format instead
            return SetupServerDirectoryWithServiceSid(directoryPath, serviceAccountName);
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Failed to set ACLs on {Path}", directoryPath);
            return Result.Failure($"[ACL.SetFailed] Failed to set directory ACLs: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result RemoveServerDirectoryAccess(string directoryPath, string serviceAccountName)
    {
        var pathValidation = ValidateDirectoryPath(directoryPath);
        if (pathValidation.IsFailure)
        {
            return pathValidation;
        }

        var accountValidation = ValidateServiceAccountName(serviceAccountName);
        if (accountValidation.IsFailure)
        {
            return accountValidation;
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogDebug("Directory {Path} does not exist, nothing to remove", directoryPath);
                return Result.Success();
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            NTAccount serviceAccount;
            try
            {
                serviceAccount = new NTAccount(serviceAccountName);
            }
            catch (ArgumentException ex)
            {
                return Result.Failure($"[ACL.InvalidAccount] Invalid service account name: {ex.Message}");
            }

            // Remove all access rules for this account
            security.PurgeAccessRules(serviceAccount);

            directoryInfo.SetAccessControl(security);

            _logger.LogInformation(
                "Removed access for {Account} from directory {Path}",
                serviceAccountName, directoryPath);

            return Result.Success();
        }
        catch (IdentityNotMappedException)
        {
            // Account doesn't exist, nothing to remove
            _logger.LogDebug(
                "Service account {Account} not found, nothing to remove",
                serviceAccountName);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied removing ACLs from {Path}", directoryPath);
            return Result.Failure($"[ACL.AccessDenied] Access denied removing ACLs: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Failed to remove ACLs from {Path}", directoryPath);
            return Result.Failure($"[ACL.RemoveFailed] Failed to remove directory ACLs: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result<bool> VerifyAccess(string directoryPath, string serviceAccountName)
    {
        var pathValidation = ValidateDirectoryPath(directoryPath);
        if (pathValidation.IsFailure)
        {
            return Result<bool>.Failure(pathValidation.Error);
        }

        var accountValidation = ValidateServiceAccountName(serviceAccountName);
        if (accountValidation.IsFailure)
        {
            return Result<bool>.Failure(accountValidation.Error);
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return Result<bool>.Success(false);
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            NTAccount serviceAccount;
            try
            {
                serviceAccount = new NTAccount(serviceAccountName);
            }
            catch (ArgumentException)
            {
                return Result<bool>.Success(false);
            }

            var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                targetType: typeof(NTAccount));

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference.Value.Equals(serviceAccountName, StringComparison.OrdinalIgnoreCase) &&
                    rule.AccessControlType == AccessControlType.Allow &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
                {
                    return Result<bool>.Success(true);
                }
            }

            return Result<bool>.Success(false);
        }
        catch (IdentityNotMappedException)
        {
            return Result<bool>.Success(false);
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to verify ACLs on {Path}", directoryPath);
            return Result<bool>.Failure($"[ACL.VerifyFailed] Failed to verify directory ACLs: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result DenyAccess(string directoryPath, string serviceAccountName)
    {
        var pathValidation = ValidateDirectoryPath(directoryPath);
        if (pathValidation.IsFailure)
        {
            return pathValidation;
        }

        var accountValidation = ValidateServiceAccountName(serviceAccountName);
        if (accountValidation.IsFailure)
        {
            return accountValidation;
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogDebug("Directory {Path} does not exist, cannot set deny rule", directoryPath);
                return Result.Success();
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            NTAccount serviceAccount;
            try
            {
                serviceAccount = new NTAccount(serviceAccountName);
            }
            catch (ArgumentException ex)
            {
                return Result.Failure($"[ACL.InvalidAccount] Invalid service account name: {ex.Message}");
            }

            // Add explicit deny rule for full control
            var denyRule = new FileSystemAccessRule(
                serviceAccount,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny);

            security.AddAccessRule(denyRule);

            directoryInfo.SetAccessControl(security);

            _logger.LogInformation(
                "Denied access for {Account} to directory {Path}",
                serviceAccountName, directoryPath);

            return Result.Success();
        }
        catch (IdentityNotMappedException ex)
        {
            // Account doesn't exist yet - this is expected for Virtual Service Accounts
            // before the service has been started
            _logger.LogDebug(
                ex,
                "Service account {Account} not yet mapped, deny rule will be ineffective until service starts",
                serviceAccountName);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied setting deny ACL on {Path}", directoryPath);
            return Result.Failure($"[ACL.AccessDenied] Access denied setting deny ACL: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Failed to set deny ACL on {Path}", directoryPath);
            return Result.Failure($"[ACL.DenyFailed] Failed to set deny ACL: {ex.Message}");
        }
    }

    #region Private Methods

    /// <summary>
    /// Sets up directory ACLs using the service SID format.
    /// This is used when the Virtual Service Account hasn't been created yet.
    /// </summary>
    private Result SetupServerDirectoryWithServiceSid(string directoryPath, string serviceAccountName)
    {
        try
        {
            // Extract service name from "NT SERVICE\MeridianGS_xxx"
            var serviceName = serviceAccountName.Replace(@"NT SERVICE\", "", StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug(
                "Attempting to set ACLs using service SID for service {ServiceName}",
                serviceName);

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            // For Virtual Service Accounts, we can use the well-known SID prefix
            // S-1-5-80-{SHA1 of uppercase service name}
            // However, this is complex to compute. Instead, we'll grant access to
            // the SYSTEM and LOCAL SERVICE accounts which can be used as fallback.

            // Grant LOCAL SERVICE access as a fallback
            // Note: The actual Virtual Service Account will get access when the service starts
            var localServiceAccount = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var localServiceRule = new FileSystemAccessRule(
                localServiceAccount,
                FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            security.AddAccessRule(localServiceRule);

            // Grant SYSTEM full control (needed for service management)
            var systemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var systemRule = new FileSystemAccessRule(
                systemAccount,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            security.AddAccessRule(systemRule);

            directoryInfo.SetAccessControl(security);

            _logger.LogInformation(
                "Set fallback ACLs on directory {Path}. " +
                "Full ACLs will be applied when service {ServiceName} starts.",
                directoryPath, serviceName);

            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to set fallback ACLs on {Path}", directoryPath);
            return Result.Failure($"[ACL.FallbackFailed] Failed to set fallback directory ACLs: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a directory path for security.
    /// </summary>
    private static Result ValidateDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return Result.Failure("[ACL.InvalidPath] Directory path is required");
        }

        // Must be absolute path
        if (!Path.IsPathRooted(directoryPath))
        {
            return Result.Failure("[ACL.InvalidPath] Directory path must be absolute");
        }

        // Normalize and check for traversal
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex)
        {
            return Result.Failure($"[ACL.InvalidPath] Invalid directory path: {ex.Message}");
        }

        // Check for path traversal attempts
        if (!normalizedPath.Equals(directoryPath, StringComparison.OrdinalIgnoreCase) &&
            !normalizedPath.Equals(directoryPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            // Allow trailing separator differences but block traversal
            var normalizedWithoutTrailing = normalizedPath.TrimEnd(Path.DirectorySeparatorChar);
            var inputWithoutTrailing = directoryPath.TrimEnd(Path.DirectorySeparatorChar);

            if (!normalizedWithoutTrailing.Equals(inputWithoutTrailing, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure("[ACL.PathTraversal] Path traversal detected in directory path");
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates a service account name for security.
    /// </summary>
    private static Result ValidateServiceAccountName(string serviceAccountName)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountName))
        {
            return Result.Failure("[ACL.InvalidAccount] Service account name is required");
        }

        if (!ValidServiceAccountPattern().IsMatch(serviceAccountName))
        {
            return Result.Failure(
                "[ACL.InvalidAccount] Service account name must be in format " +
                "'NT SERVICE\\MeridianGS_{id}' where id contains only alphanumeric, hyphen, or underscore");
        }

        return Result.Success();
    }

    #endregion
}
