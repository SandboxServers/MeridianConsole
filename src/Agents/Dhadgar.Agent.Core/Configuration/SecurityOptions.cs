using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Security configuration for the agent.
/// </summary>
public sealed class SecurityOptions
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
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Path to the agent certificate file for mTLS.
    /// Used by Linux agent for file-based certificate storage.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Path to the agent private key file for mTLS.
    /// Used by Linux agent for file-based certificate storage.
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
}
