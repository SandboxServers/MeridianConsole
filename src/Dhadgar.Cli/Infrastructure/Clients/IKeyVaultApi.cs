using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Key Vault Service API calls
/// </summary>
public interface IKeyVaultApi
{
    [Get("/api/v1/keyvaults")]
    Task<KeyVaultListResponse> GetVaultsAsync(CancellationToken ct = default);

    [Get("/api/v1/keyvaults/{vaultName}")]
    Task<KeyVaultResponse> GetVaultAsync(string vaultName, CancellationToken ct = default);

    [Post("/api/v1/keyvaults")]
    Task<KeyVaultCreateResponse> CreateVaultAsync([Body] CreateVaultRequest request, CancellationToken ct = default);

    [Patch("/api/v1/keyvaults/{vaultName}")]
    Task<KeyVaultResponse> UpdateVaultAsync(string vaultName, [Body] UpdateVaultRequest request, CancellationToken ct = default);

    [Delete("/api/v1/keyvaults/{vaultName}")]
    Task DeleteVaultAsync(string vaultName, CancellationToken ct = default);
}

public class KeyVaultListResponse
{
    [JsonPropertyName("vaults")]
    public Collection<KeyVaultSummary> Vaults { get; set; } = new();
}

public class KeyVaultSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("secretCount")]
    public int SecretCount { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class KeyVaultResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("resourceGroup")]
    public string ResourceGroup { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("secretCount")]
    public int SecretCount { get; set; }

    [JsonPropertyName("keyCount")]
    public int KeyCount { get; set; }

    [JsonPropertyName("certificateCount")]
    public int CertificateCount { get; set; }

    [JsonPropertyName("softDeleteEnabled")]
    public bool SoftDeleteEnabled { get; set; }

    [JsonPropertyName("softDeleteRetentionDays")]
    public int SoftDeleteRetentionDays { get; set; }

    [JsonPropertyName("purgeProtectionEnabled")]
    public bool PurgeProtectionEnabled { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class CreateVaultRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}

public class KeyVaultCreateResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("resourceId")]
    public string ResourceId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class UpdateVaultRequest
{
    [JsonPropertyName("softDeleteEnabled")]
    public bool? SoftDeleteEnabled { get; set; }

    [JsonPropertyName("purgeProtectionEnabled")]
    public bool? PurgeProtectionEnabled { get; set; }

    [JsonPropertyName("softDeleteRetentionDays")]
    public int? SoftDeleteRetentionDays { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }
}
