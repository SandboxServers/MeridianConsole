using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Secrets Service API calls
/// </summary>
public interface ISecretsApi
{
    [Get("/api/v1/secrets")]
    Task<SecretListResponse> GetSecretsAsync(CancellationToken ct = default);

    [Get("/api/v1/secrets/{name}")]
    Task<SecretResponse> GetSecretAsync(string name, CancellationToken ct = default);

    [Put("/api/v1/secrets/{name}")]
    Task<SecretSetResponse> SetSecretAsync(string name, [Body] SetSecretRequest request, CancellationToken ct = default);

    [Post("/api/v1/secrets/{name}/rotate")]
    Task<SecretRotateResponse> RotateSecretAsync(string name, CancellationToken ct = default);

    [Delete("/api/v1/secrets/{name}")]
    Task DeleteSecretAsync(string name, CancellationToken ct = default);

    [Get("/api/v1/certificates")]
    Task<CertificateListResponse> GetCertificatesAsync(CancellationToken ct = default);

    [Post("/api/v1/certificates")]
    Task<CertificateImportResponse> ImportCertificateAsync([Body] ImportCertificateRequest request, CancellationToken ct = default);
}

public class SecretListResponse
{
    [JsonPropertyName("secrets")]
    public Collection<SecretInfo> Secrets { get; set; } = new();
}

public class SecretInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }
}

public class SecretResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class SetSecretRequest
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SecretSetResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class SecretRotateResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("newVersion")]
    public int NewVersion { get; set; }

    [JsonPropertyName("rotatedAt")]
    public DateTime RotatedAt { get; set; }
}

public class CertificateListResponse
{
    [JsonPropertyName("certificates")]
    public Collection<CertificateInfo> Certificates { get; set; } = new();
}

public class CertificateInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; set; } = string.Empty;
}

public class ImportCertificateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("certificateData")]
    public string CertificateData { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public class CertificateImportResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; set; } = string.Empty;

    [JsonPropertyName("importedAt")]
    public DateTime ImportedAt { get; set; }
}
