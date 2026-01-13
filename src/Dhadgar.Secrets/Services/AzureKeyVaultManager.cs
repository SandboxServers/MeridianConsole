using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.KeyVault.Models;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Certificates;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public sealed class AzureKeyVaultManager : IKeyVaultManager
{
    private readonly ArmClient _armClient;
    private readonly string _subscriptionId;
    private readonly ILogger<AzureKeyVaultManager> _logger;
    private readonly DefaultAzureCredential _credential;

    public AzureKeyVaultManager(
        IOptions<SecretsOptions> options,
        ILogger<AzureKeyVaultManager> logger)
    {
        _logger = logger;
        _credential = new DefaultAzureCredential();
        _armClient = new ArmClient(_credential);

        // Get subscription ID from configuration or environment
        _subscriptionId = options.Value.AzureSubscriptionId
            ?? Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("Azure subscription ID not configured");

        _logger.LogInformation("Initialized Azure Key Vault Manager for subscription {SubscriptionId}", _subscriptionId);
    }

    public async Task<List<VaultSummary>> ListVaultsAsync(CancellationToken ct = default)
    {
        try
        {
            var subscription = await _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_subscriptionId}")).GetAsync(ct);
            var vaults = new List<VaultSummary>();

            await foreach (var vault in subscription.Value.GetKeyVaultsAsync(cancellationToken: ct))
            {
                var vaultUri = vault.Data.Properties.VaultUri;
                if (vaultUri is null)
                {
                    _logger.LogWarning("Vault {VaultName} does not expose a URI; skipping secret count.", vault.Data.Name);
                    continue;
                }

                var secretCount = await CountSecretsAsync(vaultUri, ct);

                vaults.Add(new VaultSummary(
                    Name: vault.Data.Name,
                    VaultUri: vaultUri,
                    Location: vault.Data.Location.Name,
                    SecretCount: secretCount,
                    Enabled: true // Vaults don't have a direct "enabled" property
                ));
            }

            _logger.LogInformation("Listed {Count} Key Vaults", vaults.Count);
            return vaults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Key Vaults");
            throw;
        }
    }

    public async Task<VaultDetail?> GetVaultAsync(string vaultName, CancellationToken ct = default)
    {
        try
        {
            var subscription = await _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_subscriptionId}")).GetAsync(ct);

            // Find vault by name across all resource groups
            KeyVaultResource? targetVault = null;
            await foreach (var vault in subscription.Value.GetKeyVaultsAsync(cancellationToken: ct))
            {
                if (vault.Data.Name.Equals(vaultName, StringComparison.OrdinalIgnoreCase))
                {
                    targetVault = vault;
                    break;
                }
            }

            if (targetVault == null)
            {
                _logger.LogWarning("Vault {VaultName} not found", vaultName);
                return null;
            }

            var vaultUri = targetVault.Data.Properties.VaultUri;
            if (vaultUri is null)
            {
                _logger.LogWarning("Vault {VaultName} does not expose a URI.", vaultName);
                return null;
            }

            var secretCount = await CountSecretsAsync(vaultUri, ct);
            var keyCount = await CountKeysAsync(vaultUri, ct);
            var certCount = await CountCertificatesAsync(vaultUri, ct);

            // Extract resource group from ID
            var resourceGroupName = targetVault.Id.ResourceGroupName ?? "Unknown";

            var detail = new VaultDetail(
                Name: targetVault.Data.Name,
                VaultUri: vaultUri,
                Location: targetVault.Data.Location.Name,
                ResourceGroup: resourceGroupName,
                Sku: targetVault.Data.Properties.Sku.Name.ToString(),
                TenantId: targetVault.Data.Properties.TenantId.ToString(),
                EnableSoftDelete: targetVault.Data.Properties.EnableSoftDelete ?? false,
                EnablePurgeProtection: targetVault.Data.Properties.EnablePurgeProtection ?? false,
                SoftDeleteRetentionDays: targetVault.Data.Properties.SoftDeleteRetentionInDays ?? 90,
                EnableRbacAuthorization: targetVault.Data.Properties.EnableRbacAuthorization ?? false,
                PublicNetworkAccess: targetVault.Data.Properties.PublicNetworkAccess ?? "Enabled",
                SecretCount: secretCount,
                KeyCount: keyCount,
                CertificateCount: certCount,
                CreatedAt: targetVault.Data.Properties.CreateMode.HasValue ? DateTime.UtcNow : DateTime.UtcNow, // ARM doesn't expose creation time directly
                UpdatedAt: DateTime.UtcNow
            );

            _logger.LogInformation("Retrieved details for vault {VaultName}", vaultName);
            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get vault {VaultName}", vaultName);
            throw;
        }
    }

    public async Task<VaultDetail> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default)
    {
        try
        {
            // Validate vault name
            if (request.Name.Length < 3 || request.Name.Length > 24)
            {
                throw new ArgumentException("Vault name must be 3-24 characters");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Name, @"^[a-zA-Z0-9-]+$"))
            {
                throw new ArgumentException("Vault name can only contain letters, numbers, and hyphens");
            }

            var subscription = await _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_subscriptionId}")).GetAsync(ct);

            // Get or create resource group
            var resourceGroupName = request.ResourceGroupName ?? "meridian-rg";
            var resourceGroupCollection = subscription.Value.GetResourceGroups();

            ResourceGroupResource resourceGroup;
            if (!await resourceGroupCollection.ExistsAsync(resourceGroupName, ct))
            {
                _logger.LogInformation("Creating resource group {ResourceGroup}", resourceGroupName);
                var rgData = new ResourceGroupData(new AzureLocation(request.Location));
                var rgResult = await resourceGroupCollection.CreateOrUpdateAsync(WaitUntil.Completed, resourceGroupName, rgData, ct);
                resourceGroup = rgResult.Value;
            }
            else
            {
                resourceGroup = await resourceGroupCollection.GetAsync(resourceGroupName, ct);
            }

            // Get tenant ID
            var tenantId = Guid.Parse(Environment.GetEnvironmentVariable("AZURE_TENANT_ID")
                ?? throw new InvalidOperationException("AZURE_TENANT_ID not set"));

            // Create vault properties
            var vaultProperties = new KeyVaultProperties(tenantId, new KeyVaultSku(KeyVaultSkuFamily.A, KeyVaultSkuName.Standard))
            {
                EnableSoftDelete = true,
                EnablePurgeProtection = true,
                SoftDeleteRetentionInDays = 90,
                EnableRbacAuthorization = true,
                PublicNetworkAccess = "Enabled"
            };

            var vaultContent = new KeyVaultCreateOrUpdateContent(new AzureLocation(request.Location), vaultProperties);

            // Create the vault
            _logger.LogInformation("Creating Key Vault {VaultName} in {Location}", request.Name, request.Location);
            var vaultCollection = resourceGroup.GetKeyVaults();
            var operation = await vaultCollection.CreateOrUpdateAsync(WaitUntil.Completed, request.Name, vaultContent, ct);
            var vault = operation.Value;

            _logger.LogInformation("Created Key Vault {VaultName}", request.Name);

            // Return vault details
            var createdVaultUri = vault.Data.Properties.VaultUri
                ?? throw new InvalidOperationException($"Vault '{request.Name}' does not expose a URI.");

            return new VaultDetail(
                Name: vault.Data.Name,
                VaultUri: createdVaultUri,
                Location: vault.Data.Location.Name,
                ResourceGroup: resourceGroupName,
                Sku: vault.Data.Properties.Sku.Name.ToString(),
                TenantId: vault.Data.Properties.TenantId.ToString(),
                EnableSoftDelete: vault.Data.Properties.EnableSoftDelete ?? false,
                EnablePurgeProtection: vault.Data.Properties.EnablePurgeProtection ?? false,
                SoftDeleteRetentionDays: vault.Data.Properties.SoftDeleteRetentionInDays ?? 90,
                EnableRbacAuthorization: vault.Data.Properties.EnableRbacAuthorization ?? false,
                PublicNetworkAccess: vault.Data.Properties.PublicNetworkAccess ?? "Enabled",
                SecretCount: 0,
                KeyCount: 0,
                CertificateCount: 0,
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create vault {VaultName}", request.Name);
            throw;
        }
    }

    public async Task<VaultDetail> UpdateVaultAsync(string vaultName, UpdateVaultRequest request, CancellationToken ct = default)
    {
        try
        {
            var subscription = await _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_subscriptionId}")).GetAsync(ct);

            // Find vault
            KeyVaultResource? targetVault = null;
            await foreach (var vault in subscription.Value.GetKeyVaultsAsync(cancellationToken: ct))
            {
                if (vault.Data.Name.Equals(vaultName, StringComparison.OrdinalIgnoreCase))
                {
                    targetVault = vault;
                    break;
                }
            }

            if (targetVault == null)
            {
                throw new InvalidOperationException($"Vault '{vaultName}' not found");
            }

            // Update properties
            var properties = targetVault.Data.Properties;

            if (request.EnableSoftDelete.HasValue)
            {
                properties.EnableSoftDelete = request.EnableSoftDelete.Value;
            }

            if (request.EnablePurgeProtection.HasValue)
            {
                if (!request.EnablePurgeProtection.Value && (properties.EnablePurgeProtection ?? false))
                {
                    throw new InvalidOperationException("Purge protection cannot be disabled once enabled");
                }
                properties.EnablePurgeProtection = request.EnablePurgeProtection.Value;
            }

            if (request.SoftDeleteRetentionDays.HasValue)
            {
                if (request.SoftDeleteRetentionDays.Value < 7 || request.SoftDeleteRetentionDays.Value > 90)
                {
                    throw new ArgumentException("Soft delete retention days must be between 7 and 90");
                }
                properties.SoftDeleteRetentionInDays = request.SoftDeleteRetentionDays.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Sku))
            {
                var skuName = request.Sku.Equals("premium", StringComparison.OrdinalIgnoreCase)
                    ? KeyVaultSkuName.Premium
                    : KeyVaultSkuName.Standard;
                properties.Sku = new KeyVaultSku(KeyVaultSkuFamily.A, skuName);
            }

            // Update vault
            var vaultContent = new KeyVaultCreateOrUpdateContent(targetVault.Data.Location, properties);
            var resourceGroup = await _armClient.GetResourceGroupResource(targetVault.Id.Parent!).GetAsync(ct);
            var vaultCollection = resourceGroup.Value.GetKeyVaults();
            var operation = await vaultCollection.CreateOrUpdateAsync(WaitUntil.Completed, vaultName, vaultContent, ct);
            var updatedVault = operation.Value;

            _logger.LogInformation("Updated vault {VaultName}", vaultName);

            // Get current counts
            var vaultUri = updatedVault.Data.Properties.VaultUri
                ?? throw new InvalidOperationException($"Vault '{vaultName}' does not expose a URI.");
            var secretCount = await CountSecretsAsync(vaultUri, ct);
            var keyCount = await CountKeysAsync(vaultUri, ct);
            var certCount = await CountCertificatesAsync(vaultUri, ct);

            return new VaultDetail(
                Name: updatedVault.Data.Name,
                VaultUri: vaultUri,
                Location: updatedVault.Data.Location.Name,
                ResourceGroup: updatedVault.Id.ResourceGroupName ?? "Unknown",
                Sku: updatedVault.Data.Properties.Sku.Name.ToString(),
                TenantId: updatedVault.Data.Properties.TenantId.ToString(),
                EnableSoftDelete: updatedVault.Data.Properties.EnableSoftDelete ?? false,
                EnablePurgeProtection: updatedVault.Data.Properties.EnablePurgeProtection ?? false,
                SoftDeleteRetentionDays: updatedVault.Data.Properties.SoftDeleteRetentionInDays ?? 90,
                EnableRbacAuthorization: updatedVault.Data.Properties.EnableRbacAuthorization ?? false,
                PublicNetworkAccess: updatedVault.Data.Properties.PublicNetworkAccess ?? "Enabled",
                SecretCount: secretCount,
                KeyCount: keyCount,
                CertificateCount: certCount,
                CreatedAt: DateTime.UtcNow, // ARM doesn't expose this
                UpdatedAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update vault {VaultName}", vaultName);
            throw;
        }
    }

    public async Task<bool> DeleteVaultAsync(string vaultName, bool purge = false, CancellationToken ct = default)
    {
        try
        {
            var subscription = await _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{_subscriptionId}")).GetAsync(ct);

            // Find vault
            KeyVaultResource? targetVault = null;
            string? location = null;
            await foreach (var vault in subscription.Value.GetKeyVaultsAsync(cancellationToken: ct))
            {
                if (vault.Data.Name.Equals(vaultName, StringComparison.OrdinalIgnoreCase))
                {
                    targetVault = vault;
                    location = vault.Data.Location.Name;
                    break;
                }
            }

            if (targetVault == null)
            {
                _logger.LogWarning("Vault {VaultName} not found", vaultName);
                return false;
            }

            // Delete vault (soft delete if enabled)
            await targetVault.DeleteAsync(WaitUntil.Completed, ct);
            _logger.LogInformation("Deleted vault {VaultName}", vaultName);

            // Purge if requested and vault has soft delete enabled
            if (purge && location != null)
            {
                try
                {
                    // Wait a moment for the delete operation to be reflected
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);

                    // TODO: Purging deleted vaults requires using the Azure Management REST API directly
                    // The ARM SDK doesn't currently expose GetDeletedVaults() on SubscriptionResource
                    // For now, purge is not implemented. To purge manually, use:
                    // az keyvault purge --name {vaultName} --location {location}

                    var purgeCommand = $"az keyvault purge --name {vaultName} --location {location}";
                    _logger.LogWarning("Purge requested for vault {VaultName} but automatic purging is not yet implemented. Use '{PurgeCommand}' to purge manually.", vaultName, purgeCommand);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to purge vault {VaultName}", vaultName);
                    // Don't throw - delete was successful even if purge failed
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vault {VaultName}", vaultName);
            throw;
        }
    }

    // Helper methods to count vault contents
    private async Task<int> CountSecretsAsync(Uri vaultUri, CancellationToken ct)
    {
        try
        {
            var client = new SecretClient(vaultUri, _credential);
            var count = 0;
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(ct))
            {
                count++;
            }
            return count;
        }
        catch
        {
            return 0; // Ignore errors in counting
        }
    }

    private async Task<int> CountKeysAsync(Uri vaultUri, CancellationToken ct)
    {
        try
        {
            var client = new KeyClient(vaultUri, _credential);
            var count = 0;
            await foreach (var _ in client.GetPropertiesOfKeysAsync(cancellationToken: ct))
            {
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> CountCertificatesAsync(Uri vaultUri, CancellationToken ct)
    {
        try
        {
            var client = new CertificateClient(vaultUri, _credential);
            var count = 0;
            await foreach (var _ in client.GetPropertiesOfCertificatesAsync(cancellationToken: ct))
            {
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
