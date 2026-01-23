using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Client certificate issued to an agent for mTLS authentication.
/// </summary>
public sealed class AgentCertificate
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>SHA-256 thumbprint of the certificate.</summary>
    [Required]
    [MaxLength(64)]
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>Certificate serial number.</summary>
    [Required]
    [MaxLength(100)]
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Certificate validity start date.</summary>
    public DateTime NotBefore { get; set; }

    /// <summary>Certificate expiration date.</summary>
    public DateTime NotAfter { get; set; }

    /// <summary>Whether this certificate has been revoked.</summary>
    public bool IsRevoked { get; set; }

    /// <summary>When the certificate was revoked (null if active).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Reason for revocation.</summary>
    [MaxLength(500)]
    public string? RevocationReason { get; set; }

    /// <summary>When this certificate was issued.</summary>
    public DateTime IssuedAt { get; set; }

    // Navigation property
    public Node Node { get; set; } = null!;
}
