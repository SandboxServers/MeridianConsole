using Azure.Security.KeyVault.Certificates;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public sealed class KeyVaultCertificateProvider : ICertificateProvider
{
    private readonly CertificateClient _client;
    private readonly ILogger<KeyVaultCertificateProvider> _logger;

    public KeyVaultCertificateProvider(
        IOptions<SecretsOptions> options,
        IWifCredentialProvider credentialProvider,
        ILogger<KeyVaultCertificateProvider> logger)
    {
        _logger = logger;

        var keyVaultUri = options.Value.KeyVaultUri;
        if (string.IsNullOrWhiteSpace(keyVaultUri))
        {
            throw new InvalidOperationException("KeyVaultUri is required for certificate operations.");
        }

        _logger.LogInformation("Initializing KeyVaultCertificateProvider with vault: {KeyVaultUri}", keyVaultUri);

        // Use WIF credential provider (falls back to DefaultAzureCredential if WIF not configured)
        var credential = credentialProvider.GetCredential();
        _client = new CertificateClient(new Uri(keyVaultUri), credential);
    }

    public async Task<List<CertificateInfo>> ListCertificatesAsync(string? vaultName = null, CancellationToken ct = default)
    {
        try
        {
            var certificates = new List<CertificateInfo>();

            await foreach (var certProperties in _client.GetPropertiesOfCertificatesAsync(cancellationToken: ct))
            {
                // Fetch full certificate to get subject and issuer information
                var cert = await _client.GetCertificateAsync(certProperties.Name, ct);

                certificates.Add(new CertificateInfo(
                    Name: certProperties.Name,
                    Subject: cert.Value.Policy?.Subject ?? "Unknown",
                    Issuer: cert.Value.Policy?.IssuerName ?? "Unknown",
                    ExpiresAt: certProperties.ExpiresOn?.UtcDateTime ?? DateTime.MaxValue,
                    Thumbprint: certProperties.X509Thumbprint != null ? Convert.ToHexString(certProperties.X509Thumbprint) : string.Empty,
                    Enabled: certProperties.Enabled ?? true
                ));
            }

            _logger.LogInformation("Listed {Count} certificates from Key Vault", certificates.Count);
            return certificates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list certificates from Key Vault");
            throw;
        }
    }

    public async Task<ImportCertificateResult> ImportCertificateAsync(
        string name,
        byte[] certificateData,
        string? password = null,
        string? vaultName = null,
        CancellationToken ct = default)
    {
        try
        {
            // Create import options
            var importOptions = new ImportCertificateOptions(name, certificateData)
            {
                Password = password,
                Enabled = true
            };

            // Import the certificate
            var response = await _client.ImportCertificateAsync(importOptions, ct);
            var certificate = response.Value;

            _logger.LogInformation("Imported certificate {Name} with thumbprint {Thumbprint}",
                name, Convert.ToHexString(certificate.Properties.X509Thumbprint ?? Array.Empty<byte>()));

            return new ImportCertificateResult(
                Name: certificate.Name,
                Subject: certificate.Policy?.Subject ?? "Unknown",
                Issuer: certificate.Policy?.IssuerName ?? "Unknown",
                Thumbprint: certificate.Properties.X509Thumbprint != null
                    ? Convert.ToHexString(certificate.Properties.X509Thumbprint)
                    : string.Empty,
                ExpiresAt: certificate.Properties.ExpiresOn?.UtcDateTime ?? DateTime.MaxValue
            );
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 400)
        {
            _logger.LogError(ex, "Invalid certificate format or password for {Name}", name);
            throw new InvalidOperationException("Invalid certificate format or incorrect password", ex);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogError(ex, "Certificate {Name} already exists", name);
            throw new InvalidOperationException($"Certificate '{name}' already exists", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import certificate {Name}", name);
            throw;
        }
    }

    public async Task<bool> DeleteCertificateAsync(string name, string? vaultName = null, CancellationToken ct = default)
    {
        try
        {
            // Start delete operation (soft delete if enabled)
            await _client.StartDeleteCertificateAsync(name, ct);

            _logger.LogInformation("Deleted certificate {Name} from Key Vault", name);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Attempted to delete non-existent certificate: {Name}", name);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete certificate {Name} from Key Vault", name);
            throw;
        }
    }
}
