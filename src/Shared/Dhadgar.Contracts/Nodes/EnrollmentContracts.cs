namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Request to create a new enrollment token for agent registration.
/// </summary>
/// <param name="Label">Optional human-readable label for the token.</param>
/// <param name="ExpiresInMinutes">Token validity in minutes (1-10080, default 60).</param>
public record CreateEnrollmentTokenRequest(
    string? Label,
    int? ExpiresInMinutes);

/// <summary>
/// Response containing a newly created enrollment token.
/// The plaintext token is only returned once and must be securely transmitted to the agent.
/// </summary>
/// <param name="TokenId">Unique identifier for the token record.</param>
/// <param name="Token">The plaintext enrollment token (shown only once).</param>
/// <param name="ExpiresAt">When the token expires.</param>
public record CreateEnrollmentTokenResponse(
    string TokenId,
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Summary of an enrollment token for listing.
/// </summary>
/// <param name="Id">Unique identifier of the token.</param>
/// <param name="Label">Human-readable label, if provided.</param>
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="UsedAt">When the token was used, if consumed.</param>
/// <param name="UsedByNodeId">Node ID that consumed the token, if used.</param>
/// <param name="CreatedByUserId">User who created the token.</param>
/// <param name="CreatedAt">When the token was created.</param>
/// <param name="IsRevoked">Whether the token has been revoked.</param>
public record EnrollmentTokenResponse(
    string Id,
    string? Label,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    string? UsedByNodeId,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    bool IsRevoked);

/// <summary>
/// Request from an agent to enroll with the platform (<c>POST /agents/enroll</c>,
/// public path <c>/api/v1/agents/enroll</c>).
/// </summary>
/// <remarks>
/// This is the single wire contract for enrollment: <c>Dhadgar.Agent.Core</c> sends it and
/// <c>Dhadgar.Nodes</c> binds it. The agent generates its key pair locally and sends only
/// the public key; the private key never leaves the agent's machine.
/// </remarks>
/// <param name="Token">The one-time enrollment token.</param>
/// <param name="Platform">Operating system platform (linux or windows).</param>
/// <param name="Hardware">Hardware inventory collected from the machine.</param>
/// <param name="PublicKey">
/// Base64-encoded DER SubjectPublicKeyInfo of the agent's key pair (ECDSA P-256/P-384 or
/// RSA ≥ 2048). The issued client certificate is bound to this key.
/// </param>
/// <param name="AgentVersion">Agent build version, for diagnostics.</param>
public record EnrollNodeRequest(
    string Token,
    string Platform,
    HardwareInventoryRequest Hardware,
    string PublicKey,
    string? AgentVersion = null);

/// <summary>
/// Hardware inventory sent during agent enrollment.
/// </summary>
/// <param name="Hostname">Network hostname of the machine.</param>
/// <param name="OsVersion">Operating system version string.</param>
/// <param name="CpuCores">Number of CPU cores available.</param>
/// <param name="MemoryBytes">Total physical memory in bytes.</param>
/// <param name="DiskBytes">Total disk space in bytes.</param>
/// <param name="NetworkInterfaces">Network interface information.</param>
public record HardwareInventoryRequest(
    string Hostname,
    string? OsVersion,
    int CpuCores,
    long MemoryBytes,
    long DiskBytes,
    IReadOnlyList<NetworkInterfaceInfo>? NetworkInterfaces);

/// <summary>
/// Network interface information collected during enrollment.
/// </summary>
/// <param name="Name">Interface name (e.g., eth0, Ethernet).</param>
/// <param name="MacAddress">MAC address of the interface.</param>
/// <param name="IpAddresses">IP addresses assigned to the interface.</param>
public record NetworkInterfaceInfo(
    string Name,
    string? MacAddress,
    IReadOnlyList<string>? IpAddresses);

/// <summary>
/// Response to a successful agent enrollment.
/// </summary>
/// <param name="NodeId">The assigned node identifier. The agent must persist this.</param>
/// <param name="OrganizationId">The organization that owns the node. The agent must persist this.</param>
/// <param name="CertificateThumbprint">SHA-256 thumbprint of the issued client certificate.</param>
/// <param name="Certificate">PEM-encoded client certificate, bound to the request's public key.</param>
/// <param name="CaCertificate">PEM-encoded agent CA certificate, for the agent's trust store.</param>
/// <param name="NotBefore">Certificate validity start.</param>
/// <param name="NotAfter">Certificate validity end.</param>
public record EnrollNodeResponse(
    Guid NodeId,
    Guid OrganizationId,
    string CertificateThumbprint,
    string Certificate,
    string CaCertificate,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);

/// <summary>
/// Request from an agent to renew its client certificate (<c>POST /agents/{nodeId}/certificates/renew</c>,
/// public path <c>/api/v1/agents/{nodeId}/certificates/renew</c>). Sent over mTLS with the current
/// certificate; the current thumbprint is taken from that certificate, not from the body.
/// </summary>
/// <param name="PublicKey">Base64 DER SubjectPublicKeyInfo of a NEW key pair generated on the agent.</param>
public record RenewCertificateRequest(string PublicKey);

/// <summary>Response to a successful renewal. No private key or PKCS#12 is ever returned.</summary>
/// <param name="CertificateThumbprint">SHA-256 thumbprint of the new certificate.</param>
/// <param name="Certificate">PEM-encoded new certificate, bound to the request's public key.</param>
/// <param name="CaCertificate">PEM-encoded agent CA certificate.</param>
/// <param name="NotBefore">Validity start.</param>
/// <param name="NotAfter">Validity end.</param>
public record RenewCertificateResponse(
    string CertificateThumbprint,
    string Certificate,
    string CaCertificate,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
