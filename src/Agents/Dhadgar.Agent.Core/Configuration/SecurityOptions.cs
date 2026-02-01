using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Security configuration for the agent.
/// </summary>
public sealed class SecurityOptions : IValidatableObject
{
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

        // Check if neither certificate option is configured
        if (!hasThumbprint && !hasCertPath && !hasKeyPath)
        {
            yield return new ValidationResult(
                $"Either {nameof(CertificateThumbprint)} or both {nameof(CertificatePath)} and {nameof(PrivateKeyPath)} must be specified for mTLS authentication",
                [nameof(CertificateThumbprint), nameof(CertificatePath), nameof(PrivateKeyPath)]);
            yield break;
        }

        // Check if thumbprint is used together with file-based paths
        if (hasThumbprint && (hasCertPath || hasKeyPath))
        {
            yield return new ValidationResult(
                $"{nameof(CertificateThumbprint)} cannot be used together with {nameof(CertificatePath)} or {nameof(PrivateKeyPath)}. Use either thumbprint-based (Windows) or file-based (Linux) certificate configuration",
                [nameof(CertificateThumbprint), nameof(CertificatePath), nameof(PrivateKeyPath)]);
            yield break;
        }

        // Check if only one of CertificatePath/PrivateKeyPath is provided
        if (hasCertPath != hasKeyPath)
        {
            yield return new ValidationResult(
                $"Both {nameof(CertificatePath)} and {nameof(PrivateKeyPath)} must be specified together for file-based certificate configuration",
                [nameof(CertificatePath), nameof(PrivateKeyPath)]);
        }
    }
}
