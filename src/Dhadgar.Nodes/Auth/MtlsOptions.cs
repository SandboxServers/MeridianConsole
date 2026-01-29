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
    /// Default: false (disabled for development).
    /// </summary>
    /// <remarks>
    /// In production, this should always be true. Disabling mTLS allows testing without
    /// configuring client certificates.
    /// </remarks>
    public bool Enabled { get; set; }

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
    public string SpiffeTrustDomain { get; set; } = "meridianconsole.com";

    /// <summary>
    /// The path prefix for agent endpoints that require mTLS authentication.
    /// Default: "/api/v1/agents".
    /// </summary>
    public string AgentEndpointPrefix { get; set; } = "/api/v1/agents";

    /// <summary>
    /// Paths that are exempt from mTLS authentication (relative to AgentEndpointPrefix).
    /// These endpoints use other authentication methods (e.g., enrollment token).
    /// </summary>
    public IReadOnlyList<string> ExemptPaths { get; set; } =
    [
        "/enroll",           // Uses one-time enrollment token
        "/ca-certificate"    // Public endpoint to fetch CA certificate
    ];
}
