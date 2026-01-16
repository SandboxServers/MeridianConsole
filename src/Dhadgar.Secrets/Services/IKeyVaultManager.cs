namespace Dhadgar.Secrets.Services;

public record VaultSummary(
    string Name,
    Uri VaultUri,
    string Location,
    int SecretCount,
    bool Enabled);

public record VaultDetail(
    string Name,
    Uri VaultUri,
    string Location,
    string ResourceGroup,
    string Sku,
    string TenantId,
    bool EnableSoftDelete,
    bool EnablePurgeProtection,
    int SoftDeleteRetentionDays,
    bool EnableRbacAuthorization,
    string PublicNetworkAccess,
    int SecretCount,
    int KeyCount,
    int CertificateCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateVaultRequest(
    string Name,
    string Location,
    string? ResourceGroupName = null);

public record UpdateVaultRequest(
    bool? EnableSoftDelete = null,
    bool? EnablePurgeProtection = null,
    int? SoftDeleteRetentionDays = null,
    string? Sku = null);

public interface IKeyVaultManager
{
    Task<List<VaultSummary>> ListVaultsAsync(CancellationToken ct = default);
    Task<VaultDetail?> GetVaultAsync(string vaultName, CancellationToken ct = default);
    Task<VaultDetail> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default);
    Task<VaultDetail> UpdateVaultAsync(string vaultName, UpdateVaultRequest request, CancellationToken ct = default);
    Task<bool> DeleteVaultAsync(string vaultName, bool purge = false, CancellationToken ct = default);
}
