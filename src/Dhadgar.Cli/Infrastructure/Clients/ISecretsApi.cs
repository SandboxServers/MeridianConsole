using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Secrets Service API calls
/// </summary>
public interface ISecretsApi
{
    [Get("/api/v1/secrets/{name}")]
    Task<SecretResponse> GetSecretAsync(string name, CancellationToken ct = default);

    [Get("/api/v1/secrets/oauth")]
    Task<SecretsResponse> GetOAuthSecretsAsync(CancellationToken ct = default);

    [Get("/api/v1/secrets/betterauth")]
    Task<SecretsResponse> GetBetterAuthSecretsAsync(CancellationToken ct = default);

    [Get("/api/v1/secrets/infrastructure")]
    Task<SecretsResponse> GetInfrastructureSecretsAsync(CancellationToken ct = default);

    [Post("/api/v1/secrets/batch")]
    Task<SecretsResponse> GetSecretsBatchAsync([Body] BatchSecretsRequest request, CancellationToken ct = default);

    [Put("/api/v1/secrets/{name}")]
    Task<SetSecretResponse> SetSecretAsync(string name, [Body] SetSecretRequest request, CancellationToken ct = default);

    [Post("/api/v1/secrets/{name}/rotate")]
    Task<RotateSecretResponse> RotateSecretAsync(string name, CancellationToken ct = default);

    [Delete("/api/v1/secrets/{name}")]
    Task DeleteSecretAsync(string name, CancellationToken ct = default);

    [Get("/api/v1/certificates")]
    Task<CertificateListResponse> GetCertificatesAsync(CancellationToken ct = default);

    [Get("/api/v1/keyvaults/{vaultName}/certificates")]
    Task<CertificateListResponse> GetVaultCertificatesAsync(string vaultName, CancellationToken ct = default);

    [Post("/api/v1/certificates")]
    Task<CertificateImportResponse> ImportCertificateAsync([Body] ImportCertificateRequest request, CancellationToken ct = default);

    [Post("/api/v1/keyvaults/{vaultName}/certificates")]
    Task<CertificateImportResponse> ImportVaultCertificateAsync(
        string vaultName,
        [Body] ImportCertificateRequest request,
        CancellationToken ct = default);
}

public class SecretResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SecretsResponse
{
    [JsonPropertyName("secrets")]
    public Dictionary<string, string> Secrets { get; set; } = new();
}

public class BatchSecretsRequest
{
    [JsonPropertyName("secretNames")]
    public IReadOnlyList<string> SecretNames { get; set; } = Array.Empty<string>();
}

public class SetSecretRequest
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SetSecretResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("updated")]
    public bool Updated { get; set; }
}

public class RotateSecretResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("rotatedAt")]
    public DateTime RotatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
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

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
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

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}
