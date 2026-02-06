using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
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
    private readonly IReadOnlyList<string> _allowedRoots;

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
    /// <param name="allowedRoots">List of allowed root directories. Must contain at least one entry (fail-closed security).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> or <paramref name="allowedRoots"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="allowedRoots"/> is empty.</exception>
    public DirectoryAclManager(ILogger<DirectoryAclManager> logger, IReadOnlyList<string> allowedRoots)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Combined null and count check using required guard pattern (fail-closed security)
        if (allowedRoots is not { Count: > 0 })
        {
            throw new ArgumentException("Allowed roots must be specified and contain at least one entry (fail-closed security)", nameof(allowedRoots));
        }

        _allowedRoots = allowedRoots;
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

            // Break inheritance from parent directory to ensure complete isolation
            // isProtected: true = block inheritance, preserveInheritance: false = remove inherited ACEs
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Define inheritance flags for rules that apply to this folder, subfolders, and files
            const InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            const PropagationFlags propagationFlags = PropagationFlags.None;

            // Add FullControl for SYSTEM account (required for Windows services)
            var systemAccount = new NTAccount("NT AUTHORITY", "SYSTEM");
            var systemRule = new FileSystemAccessRule(
                systemAccount,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow);
            security.AddAccessRule(systemRule);

            // Add FullControl for Administrators group (required for management)
            // Use SID instead of NTAccount to avoid localization issues on non-English Windows
            var administratorsAccount = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var administratorsRule = new FileSystemAccessRule(
                administratorsAccount,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow);
            security.AddAccessRule(administratorsRule);

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

            // Ensure we don't accumulate duplicate ACEs for repeated calls
            security.PurgeAccessRules(serviceAccount);

            // Add FullControl for the game server's service account
            var accessRule = new FileSystemAccessRule(
                serviceAccount,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
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

            // Check Deny ACEs first - they take precedence over Allow
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference.Value.Equals(serviceAccountName, StringComparison.OrdinalIgnoreCase) &&
                    rule.AccessControlType == AccessControlType.Deny &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
                {
                    // Explicit deny - access is not properly configured
                    return Result<bool>.Success(false);
                }
            }

            // Now check for Allow ACEs
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
            // Account doesn't exist yet - returning failure because the deny rule was NOT applied.
            // Returning success would signal the directory is protected when it isn't.
            _logger.LogWarning(
                ex,
                "Service account {Account} not yet mapped - deny rule could not be applied. Directory {Path} is not protected from this account.",
                serviceAccountName, directoryPath);
            return Result.Failure(
                $"[ACL.AccountNotMapped] Service account '{serviceAccountName}' not yet mapped - deny rule not applied. " +
                "Directory is not protected from this account until the service has been started.");
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
            var backslashIndex = serviceAccountName.IndexOf('\\');
            var serviceName = serviceAccountName[(backslashIndex + 1)..];

            _logger.LogDebug(
                "Attempting to set ACLs using service SID for service {ServiceName}",
                serviceName);

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();

            // Break inheritance from parent directory to ensure complete isolation
            // isProtected: true = block inheritance, preserveInheritance: false = remove inherited ACEs
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Define inheritance flags for rules that apply to this folder, subfolders, and files
            const InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            const PropagationFlags propagationFlags = PropagationFlags.None;

            // Grant SYSTEM full control (needed for service management)
            var systemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var systemRule = new FileSystemAccessRule(
                systemAccount,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow);
            security.AddAccessRule(systemRule);

            // Grant Administrators full control (needed for management)
            var administratorsAccount = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var administratorsRule = new FileSystemAccessRule(
                administratorsAccount,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow);
            security.AddAccessRule(administratorsRule);

            // Compute the Virtual Service Account SID
            // Format: S-1-5-80-{five 32-bit words from SHA1 of uppercase service name}
            var vsaSid = ComputeVirtualServiceAccountSid(serviceName);
            if (vsaSid is null)
            {
                _logger.LogWarning(
                    "Could not compute VSA SID for service {ServiceName} - cannot set ACLs without service account SID",
                    serviceName);
                return Result.Failure($"[ACL.SidComputeFailed] Could not compute Virtual Service Account SID for service: {serviceName}");
            }

            // Ensure we don't accumulate duplicate ACEs for repeated calls
            security.PurgeAccessRules(vsaSid);

            var vsaRule = new FileSystemAccessRule(
                vsaSid,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow);

            security.AddAccessRule(vsaRule);

            _logger.LogDebug(
                "Added ACL for computed VSA SID {Sid} for service {ServiceName}",
                vsaSid.Value, serviceName);

            directoryInfo.SetAccessControl(security);

            _logger.LogInformation(
                "Set ACLs on directory {Path} for service {ServiceName}.",
                directoryPath, serviceName);

            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to set ACLs on {Path}", directoryPath);
            return Result.Failure($"[ACL.FallbackFailed] Failed to set directory ACLs: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes the Security Identifier (SID) for a Virtual Service Account.
    /// </summary>
    /// <remarks>
    /// Virtual Service Account SIDs are in the format: S-1-5-80-{five 32-bit words from SHA1 of uppercase service name}
    /// Reference: https://docs.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights
    /// Note: SHA1 is mandated by Windows for VSA SID computation; this is not a security concern
    /// as it's used for identity derivation, not cryptographic security.
    /// </remarks>
    /// <param name="serviceName">The service name (without NT SERVICE\ prefix).</param>
    /// <returns>The SID, or null if computation fails.</returns>
    private SecurityIdentifier? ComputeVirtualServiceAccountSid(string serviceName)
    {
        try
        {
            // Convert service name to uppercase and get UTF-16LE bytes
            var upperName = serviceName.ToUpperInvariant();
            var nameBytes = Encoding.Unicode.GetBytes(upperName);

            // Compute SHA1 hash - SHA1 is mandated by Windows for VSA SID computation;
            // this is not a security concern as it's used for identity derivation
#pragma warning disable CA5350 // SHA1 is required by Windows for VSA SID computation
            var hashBytes = SHA1.HashData(nameBytes);
#pragma warning restore CA5350

            // Convert first 20 bytes of hash to 5 32-bit integers (little-endian)
            var subAuth1 = BitConverter.ToUInt32(hashBytes, 0);
            var subAuth2 = BitConverter.ToUInt32(hashBytes, 4);
            var subAuth3 = BitConverter.ToUInt32(hashBytes, 8);
            var subAuth4 = BitConverter.ToUInt32(hashBytes, 12);
            var subAuth5 = BitConverter.ToUInt32(hashBytes, 16);

            // Build the SID string: S-1-5-80-{subAuth1}-{subAuth2}-{subAuth3}-{subAuth4}-{subAuth5}
            var sidString = FormattableString.Invariant(
                $"S-1-5-80-{subAuth1}-{subAuth2}-{subAuth3}-{subAuth4}-{subAuth5}");

            return new SecurityIdentifier(sidString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute VSA SID for service {ServiceName}", serviceName);
            return null;
        }
    }

    /// <summary>
    /// Validates a directory path for security.
    /// </summary>
    private Result ValidateDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return Result.Failure("[ACL.InvalidPath] Directory path is required");
        }

        // Must be fully qualified path (rejects drive-relative paths like "C:foo")
        if (!Path.IsPathFullyQualified(directoryPath))
        {
            return Result.Failure("[ACL.InvalidPath] Directory path must be a fully qualified absolute path");
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
        // Trim both directory separators to handle mixed separators (e.g., "C:\Servers/sub")
        var normalizedTrimmed = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputTrimmed = directoryPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);

        if (!normalizedTrimmed.Equals(inputTrimmed, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("[ACL.PathTraversal] Path traversal detected in directory path");
        }

        // Check against allowed roots (required - fail-closed security)
        var isWithinAllowedRoot = false;
        foreach (var root in _allowedRoots)
        {
            // Safely normalize each root path
            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or SecurityException)
            {
                _logger.LogWarning(ex, "Failed to normalize allowed root path: {Root}", root);
                return Result.Failure($"[ACL.InvalidRoot] Failed to normalize allowed root path: {root}");
            }

            if (normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                isWithinAllowedRoot = true;
                break;
            }
        }

        if (!isWithinAllowedRoot)
        {
            _logger.LogWarning(
                "Path {Path} is not within any allowed root directories",
                directoryPath);
            return Result.Failure("[ACL.PathNotAllowed] Directory path is not within allowed root directories");
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
