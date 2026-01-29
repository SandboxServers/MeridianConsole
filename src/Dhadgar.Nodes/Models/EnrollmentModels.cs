using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Request to create a new enrollment token.
/// </summary>
public sealed record CreateEnrollmentTokenRequest(
    [StringLength(100, ErrorMessage = "Label cannot exceed 100 characters")]
    string? Label,

    [Range(1, 10080, ErrorMessage = "ExpiresInMinutes must be between 1 and 10080 (1 week)")]
    int? ExpiresInMinutes);

/// <summary>
/// Response containing the plaintext token (only returned once).
/// </summary>
public sealed record CreateEnrollmentTokenResponse(
    Guid TokenId,
    string Token,
    DateTime ExpiresAt);

/// <summary>
/// Summary of an enrollment token for listing.
/// </summary>
public sealed record EnrollmentTokenSummary(
    Guid Id,
    string? Label,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    string CreatedByUserId);

/// <summary>
/// Request from an agent to enroll with the platform.
/// </summary>
public sealed record EnrollNodeRequest(
    [Required(ErrorMessage = "Token is required")]
    string Token,

    [Required(ErrorMessage = "Platform is required")]
    [RegularExpression("^(linux|windows)$", ErrorMessage = "Platform must be 'linux' or 'windows'")]
    string Platform,

    [Required(ErrorMessage = "Hardware information is required")]
    HardwareInventoryDto Hardware);

/// <summary>
/// Hardware inventory sent during enrollment.
/// </summary>
public sealed record HardwareInventoryDto(
    [Required(ErrorMessage = "Hostname is required")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "Hostname must be between 1 and 255 characters")]
    string Hostname,

    [StringLength(255, ErrorMessage = "OsVersion cannot exceed 255 characters")]
    string? OsVersion,

    [Range(1, 1024, ErrorMessage = "CpuCores must be between 1 and 1024")]
    int CpuCores,

    [Range(1, long.MaxValue, ErrorMessage = "MemoryBytes must be greater than 0")]
    long MemoryBytes,

    [Range(1, long.MaxValue, ErrorMessage = "DiskBytes must be greater than 0")]
    long DiskBytes,

    IReadOnlyList<NetworkInterfaceDto>? NetworkInterfaces);

/// <summary>
/// Network interface information.
/// </summary>
public sealed record NetworkInterfaceDto(
    [Required(ErrorMessage = "Interface name is required")]
    [StringLength(100, ErrorMessage = "Interface name cannot exceed 100 characters")]
    string Name,

    [StringLength(17, ErrorMessage = "MAC address cannot exceed 17 characters")]
    string? MacAddress,

    IReadOnlyList<string>? IpAddresses);

/// <summary>
/// Response to a successful enrollment containing credentials.
/// </summary>
public sealed record EnrollNodeResponse(
    Guid NodeId,
    string CertificateThumbprint,
    string Certificate,
    string? Pkcs12Base64 = null,
    string? Pkcs12Password = null,
    DateTime? NotBefore = null,
    DateTime? NotAfter = null);

/// <summary>
/// Request to renew an agent's certificate.
/// </summary>
public sealed record RenewCertificateRequest(
    [Required(ErrorMessage = "Current certificate thumbprint is required")]
    string CurrentThumbprint);

/// <summary>
/// Response to a successful certificate renewal.
/// </summary>
public sealed record RenewCertificateResponse(
    string CertificateThumbprint,
    string Certificate,
    string Pkcs12Base64,
    string Pkcs12Password,
    DateTime NotBefore,
    DateTime NotAfter);
