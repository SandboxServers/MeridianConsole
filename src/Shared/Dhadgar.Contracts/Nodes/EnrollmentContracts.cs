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
/// Request from an agent to enroll with the platform.
/// </summary>
/// <param name="Token">The enrollment token.</param>
/// <param name="Platform">Operating system platform (linux or windows).</param>
/// <param name="Hardware">Hardware inventory collected from the machine.</param>
public record EnrollNodeRequest(
    string Token,
    string Platform,
    HardwareInventoryRequest Hardware);

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
/// Response to a successful agent enrollment containing credentials.
/// </summary>
/// <param name="NodeId">The assigned node identifier.</param>
/// <param name="CertificateThumbprint">Thumbprint of the issued certificate.</param>
/// <param name="Certificate">PEM-encoded certificate.</param>
/// <param name="Pkcs12Base64">Base64-encoded PKCS#12 bundle (certificate + key).</param>
/// <param name="Pkcs12Password">Password for the PKCS#12 bundle.</param>
/// <param name="NotBefore">Certificate validity start.</param>
/// <param name="NotAfter">Certificate validity end.</param>
public record EnrollNodeResponse(
    string NodeId,
    string CertificateThumbprint,
    string Certificate,
    string? Pkcs12Base64 = null,
    string? Pkcs12Password = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? NotAfter = null);

/// <summary>
/// Request to renew an agent's certificate.
/// </summary>
/// <param name="CurrentThumbprint">Thumbprint of the certificate being renewed.</param>
public record RenewCertificateRequest(
    string CurrentThumbprint);

/// <summary>
/// Response to a successful certificate renewal.
/// </summary>
/// <param name="CertificateThumbprint">Thumbprint of the new certificate.</param>
/// <param name="Certificate">PEM-encoded new certificate.</param>
/// <param name="Pkcs12Base64">Base64-encoded PKCS#12 bundle.</param>
/// <param name="Pkcs12Password">Password for the PKCS#12 bundle.</param>
/// <param name="NotBefore">Certificate validity start.</param>
/// <param name="NotAfter">Certificate validity end.</param>
public record RenewCertificateResponse(
    string CertificateThumbprint,
    string Certificate,
    string Pkcs12Base64,
    string Pkcs12Password,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
