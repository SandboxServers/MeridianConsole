using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Key Vault Service API calls
/// </summary>
public interface IKeyVaultApi
{
    [Get("/keyvaults")]
    Task<KeyVaultListResponse> GetVaultsAsync(CancellationToken ct = default);

    [Get("/keyvaults/{vaultName}")]
    Task<KeyVaultResponse> GetVaultAsync(string vaultName, CancellationToken ct = default);

    [Post("/keyvaults")]
    Task<KeyVaultResponse> CreateVaultAsync([Body] CreateVaultRequest request, CancellationToken ct = default);

    [Patch("/keyvaults/{vaultName}")]
    Task<KeyVaultResponse> UpdateVaultAsync(string vaultName, [Body] UpdateVaultRequest request, CancellationToken ct = default);

    [Delete("/keyvaults/{vaultName}")]
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

    [JsonPropertyName("vaultUri")]
    public string VaultUri { get; set; } = string.Empty;

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

    [JsonPropertyName("vaultUri")]
    public string VaultUri { get; set; } = string.Empty;

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

    [JsonPropertyName("enableSoftDelete")]
    public bool EnableSoftDelete { get; set; }

    [JsonPropertyName("softDeleteRetentionDays")]
    public int SoftDeleteRetentionDays { get; set; }

    [JsonPropertyName("enablePurgeProtection")]
    public bool EnablePurgeProtection { get; set; }

    [JsonPropertyName("enableRbacAuthorization")]
    public bool EnableRbacAuthorization { get; set; }

    [JsonPropertyName("publicNetworkAccess")]
    public string PublicNetworkAccess { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class CreateVaultRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}

public class UpdateVaultRequest
{
    [JsonPropertyName("enableSoftDelete")]
    public bool? EnableSoftDelete { get; set; }

    [JsonPropertyName("enablePurgeProtection")]
    public bool? EnablePurgeProtection { get; set; }

    [JsonPropertyName("softDeleteRetentionDays")]
    public int? SoftDeleteRetentionDays { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }
}
