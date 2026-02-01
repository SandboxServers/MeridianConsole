using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Security configuration for the agent.
/// </summary>
public sealed partial class SecurityOptions : IValidatableObject
{
    // SHA-1 thumbprint: 40 hex characters
    [GeneratedRegex("^[0-9A-Fa-f]{40}$", RegexOptions.Compiled)]
    private static partial Regex CertificateThumbprintRegex();

    /// <summary>
    /// Require all commands to be cryptographically signed.
    /// </summary>
    public bool RequireSignedCommands { get; set; } = true;

    /// <summary>
    /// Maximum age of a command timestamp before rejection (replay prevention).
    /// </summary>
    [Range(30, 300)]
    public int CommandMaxAgeSeconds { get; set; } = 60;

    /// <summary>
    /// Enable audit logging of all operations.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Path to the CA certificate for validating control plane connections.
    /// If not set, system trust store is used.
    /// </summary>
    public string? CaCertificatePath { get; set; }

    /// <summary>
    /// Certificate thumbprint for mTLS client authentication.
    /// Used by Windows agent to locate certificate in store.
    /// Mutually exclusive with CertificatePath/PrivateKeyPath.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Path to the agent certificate file for mTLS.
    /// Used by Linux agent for file-based certificate storage.
    /// Must be specified together with PrivateKeyPath.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Path to the agent private key file for mTLS.
    /// Used by Linux agent for file-based certificate storage.
    /// Must be specified together with CertificatePath.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Enable certificate auto-renewal when approaching expiry.
    /// </summary>
    public bool EnableCertificateAutoRenewal { get; set; } = true;

    /// <summary>
    /// Days before certificate expiry to trigger renewal.
    /// </summary>
    [Range(1, 30)]
    public int CertificateRenewalThresholdDays { get; set; } = 14;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasThumbprint = !string.IsNullOrEmpty(CertificateThumbprint);
        var hasCertPath = !string.IsNullOrEmpty(CertificatePath);
        var hasKeyPath = !string.IsNullOrEmpty(PrivateKeyPath);

        // NOTE: We do NOT require certificate configuration at startup.
        // Unenrolled agents start without certificates and obtain them via enrollment.
        // Certificate requirements are enforced at runtime by:
        // 1. EnrollmentService - requires successful enrollment before mTLS operations
        // 2. AgentOptions.Validate - checks certificate config only when NodeId is set

        // Check if thumbprint is used together with file-based paths (mutually exclusive)
        if (hasThumbprint && (hasCertPath || hasKeyPath))
        {
            yield return new ValidationResult(
                $"{nameof(CertificateThumbprint)} cannot be used together with {nameof(CertificatePath)} or {nameof(PrivateKeyPath)}. Use either thumbprint-based (Windows) or file-based (Linux) certificate configuration",
                [nameof(CertificateThumbprint), nameof(CertificatePath), nameof(PrivateKeyPath)]);
            yield break;
        }

        // Check if only one of CertificatePath/PrivateKeyPath is provided (must be together)
        if (hasCertPath != hasKeyPath)
        {
            yield return new ValidationResult(
                $"Both {nameof(CertificatePath)} and {nameof(PrivateKeyPath)} must be specified together for file-based certificate configuration",
                [nameof(CertificatePath), nameof(PrivateKeyPath)]);
        }

        // Validate thumbprint format (SHA-1: 40 hex characters)
        if (hasThumbprint && !CertificateThumbprintRegex().IsMatch(CertificateThumbprint!))
        {
            yield return new ValidationResult(
                $"{nameof(CertificateThumbprint)} must be a valid SHA-1 thumbprint (40 hexadecimal characters)",
                [nameof(CertificateThumbprint)]);
        }

        // Validate certificate paths are absolute and normalized (prevent path traversal attacks)
        foreach (var result in ValidateCertificatePath(CertificatePath, nameof(CertificatePath), hasCertPath))
        {
            yield return result;
        }

        foreach (var result in ValidateCertificatePath(PrivateKeyPath, nameof(PrivateKeyPath), hasKeyPath))
        {
            yield return result;
        }

        foreach (var result in ValidateCertificatePath(CaCertificatePath, nameof(CaCertificatePath), !string.IsNullOrEmpty(CaCertificatePath)))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Validates that a certificate path is absolute and normalized (no traversal sequences).
    /// </summary>
    private static IEnumerable<ValidationResult> ValidateCertificatePath(string? path, string propertyName, bool shouldValidate)
    {
        if (!shouldValidate || string.IsNullOrEmpty(path))
        {
            yield break;
        }

        if (!Path.IsPathRooted(path))
        {
            yield return new ValidationResult(
                $"{propertyName} must be an absolute path",
                [propertyName]);
            yield break;
        }

        // Check for path traversal sequences
        if (path.Contains("..", StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"{propertyName} must not contain path traversal sequences (..)",
                [propertyName]);
            yield break;
        }

        // Verify path is normalized - use helper to avoid yield in catch
        var (normalizedPath, isValid) = TryGetFullPath(path);
        if (!isValid)
        {
            yield return new ValidationResult(
                $"{propertyName} is not a valid path",
                [propertyName]);
        }
        else if (!path.Equals(normalizedPath, StringComparison.Ordinal) &&
            !path.Equals(normalizedPath!.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"{propertyName} must be a normalized absolute path (use '{normalizedPath}' instead)",
                [propertyName]);
        }
    }

    /// <summary>
    /// Attempts to get the full path, returning success/failure without throwing.
    /// </summary>
    private static (string? NormalizedPath, bool IsValid) TryGetFullPath(string path)
    {
        try
        {
            return (Path.GetFullPath(path), true);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return (null, false);
        }
    }
}
