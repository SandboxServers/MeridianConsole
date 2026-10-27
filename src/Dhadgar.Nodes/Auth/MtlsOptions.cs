using System.ComponentModel.DataAnnotations;
using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Configuration options for mTLS (mutual TLS) authentication for agent endpoints.
/// </summary>
public sealed class MtlsOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Mtls";

    /// <summary>
    /// Whether mTLS is enabled. When disabled, the middleware will skip certificate validation.
    /// Default: true (enabled for security).
    /// </summary>
    /// <remarks>
    /// This is enforced to be true in non-Development environments. Disabling mTLS is only
    /// permitted in Development mode for testing without client certificates.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to allow expired certificates. This should only be true in development/testing.
    /// Default: false.
    /// </summary>
    /// <remarks>
    /// WARNING: Enabling this in production is a security risk. Only use for debugging
    /// certificate expiration issues in controlled environments.
    /// </remarks>
    public bool AllowExpiredCertificates { get; set; }

    /// <summary>
    /// Minutes a certificate stays usable after a renewal supersedes it (D-S212), so an agent whose
    /// renewal response was lost can renew again with the presented certificate. 0 means immediate revoke.
    /// Must be between 0 and <see cref="AgentCertificateRenewalPolicy.MaxGraceMinutes"/>.
    /// Default: <see cref="AgentCertificateRenewalPolicy.DefaultGraceMinutes"/>.
    /// </summary>
    public int RenewalGraceMinutes { get; set; } = AgentCertificateRenewalPolicy.DefaultGraceMinutes;

    /// <summary>
    /// Whether to strictly require a client certificate on protected endpoints.
    /// When true, requests without a certificate will be rejected with 401.
    /// When false and mTLS is enabled, requests without a certificate are logged but allowed.
    /// Default: true.
    /// </summary>
    /// <remarks>
    /// Set to false during migration periods when some agents may not yet have certificates.
    /// Once all agents are enrolled, this should be set to true for strict enforcement.
    /// </remarks>
    public bool RequireClientCertificate { get; set; } = true;

    /// <summary>
    /// The expected SPIFFE trust domain for validating certificate SPIFFE IDs.
    /// Default: "meridianconsole.com".
    /// </summary>
    [Required(ErrorMessage = "SpiffeTrustDomain is required")]
    [StringLength(253, MinimumLength = 1, ErrorMessage = "SpiffeTrustDomain must be between 1 and 253 characters")]
    public string SpiffeTrustDomain { get; set; } = AgentCertificateProfile.SpiffeTrustDomain;

    /// <summary>
    /// The path prefix for agent endpoints that require mTLS authentication.
    /// Default: "/agents".
    /// </summary>
    /// <remarks>
    /// The Gateway adds /api/v1/nodes prefix via YARP transforms, so services
    /// should not include the /api/v1/ prefix in their own route mappings.
    /// </remarks>
    [Required(ErrorMessage = "AgentEndpointPrefix is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "AgentEndpointPrefix must be between 1 and 256 characters")]
    public string AgentEndpointPrefix { get; set; } = "/agents";

    /// <summary>
    /// Paths that are exempt from mTLS authentication (relative to AgentEndpointPrefix).
    /// These endpoints use other authentication methods (e.g., enrollment token).
    /// </summary>
    public IReadOnlyList<string> ExemptPaths { get; set; } =
    [
        "/enroll",           // Uses one-time enrollment token
        "/ca-certificate"    // Public endpoint to fetch CA certificate
    ];

    /// <summary>
    /// The agent hub path that requires mTLS authentication (ADR-0008).
    /// Default: <see cref="AgentHubProtocol.Path"/> ("/hubs/agent").
    /// </summary>
    /// <remarks>
    /// A request is protected when its path starts with this value as a whole segment, so
    /// "/hubs/agent/negotiate" is protected and "/hubs/agentx" is not.
    /// </remarks>
    public string HubPathPrefix { get; set; } = AgentHubProtocol.Path;

    /// <summary>
    /// Settings for the client certificate the Gateway forwards in <see cref="AgentCertificateProfile.ForwardedCertificateHeader"/>.
    /// </summary>
    /// <remarks>
    /// The Gateway terminates the TLS connection for agents on a DNS-only hostname and forwards the
    /// client certificate as a header (D-S25). Nodes trusts that header only from the addresses listed
    /// in <see cref="ForwardedCertificateOptions.TrustedProxies"/>. Production ingress and trust model:
    /// D-S210 (docs/analysis/sprint2-agent-nodes).
    /// </remarks>
    public ForwardedCertificateOptions ForwardedCertificate { get; set; } = new();
}

/// <summary>
/// Options for accepting a client certificate forwarded by a trusted proxy (the Gateway), D-S25.
/// </summary>
/// <remarks>
/// Disabled by default. When enabled, <see cref="TrustedProxies"/> must hold at least one valid
/// CIDR, otherwise the host refuses to start. The production ingress and trust model is D-S210 (A):
/// Gateway Kestrel on a DNS-only hostname, with Nodes trusting the Gateway's CIDR.
/// See docs/analysis/sprint2-agent-nodes.
/// </remarks>
public sealed class ForwardedCertificateOptions
{
    /// <summary>
    /// Whether the forwarded client certificate header is read at all. Default: false.
    /// When false, the header is removed from every request and ignored.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// CIDR ranges (for example "10.42.0.0/16") of the proxies allowed to forward a client certificate.
    /// Required and non-empty when <see cref="Enabled"/> is true. Pointer: D-S210.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>
    /// Maximum length, in characters, of the forwarded header value. Longer values are ignored.
    /// Default: 16384.
    /// </summary>
    public int MaxHeaderLength { get; set; } = 16384;
}
