using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Local file-based CA storage provider for development environments.
/// WARNING: Not suitable for production - CA private key is stored on disk.
/// </summary>
public sealed class LocalFileCaStorageProvider : ICaStorageProvider
{
    private readonly NodesOptions _options;
    private readonly ILogger<LocalFileCaStorageProvider> _logger;
    private readonly string _caDirectory;
    private readonly string _certPath;
    private readonly string _keyPath;

    // Lock for thread safety when accessing files
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public LocalFileCaStorageProvider(
        IOptions<NodesOptions> options,
        ILogger<LocalFileCaStorageProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        _caDirectory = _options.CaStoragePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeridianConsole",
            "CA");

        _certPath = Path.Combine(_caDirectory, "ca.crt");
        _keyPath = Path.Combine(_caDirectory, "ca.key");
    }

    public async Task<bool> ExistsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return File.Exists(_certPath) && File.Exists(_keyPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StoreAsync(X509Certificate2 certificate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(_caDirectory);

            // Export certificate (public key only)
            var certPem = certificate.ExportCertificatePem();
            await File.WriteAllTextAsync(_certPath, certPem, ct);

            // Export private key (encrypted with password from config)
            var rsaKey = certificate.GetRSAPrivateKey();
            if (rsaKey is null)
            {
                throw new InvalidOperationException("CA certificate must have an RSA private key");
            }

            var password = _options.CaKeyPassword ?? GenerateSecurePassword();
            var keyPem = rsaKey.ExportEncryptedPkcs8PrivateKeyPem(
                password.AsSpan(),
                new PbeParameters(
                    PbeEncryptionAlgorithm.Aes256Cbc,
                    HashAlgorithmName.SHA256,
                    iterationCount: 100_000));

            await File.WriteAllTextAsync(_keyPath, keyPem, ct);

            // Store the password in a separate file if it was auto-generated
            if (_options.CaKeyPassword is null)
            {
                var passwordPath = Path.Combine(_caDirectory, "ca.pwd");
                await File.WriteAllTextAsync(passwordPath, password, ct);
                _logger.LogWarning(
                    "CA key password auto-generated and stored at {PasswordPath}. " +
                    "Set CaKeyPassword in configuration for production use.",
                    passwordPath);
            }

            _logger.LogInformation(
                "CA certificate and key stored at {Directory}",
                _caDirectory);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<X509Certificate2> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_certPath) || !File.Exists(_keyPath))
            {
                throw new InvalidOperationException("CA certificate or key file not found");
            }

            var certPem = await File.ReadAllTextAsync(_certPath, ct);
            var keyPem = await File.ReadAllTextAsync(_keyPath, ct);

            // Get password
            var password = _options.CaKeyPassword;
            if (password is null)
            {
                var passwordPath = Path.Combine(_caDirectory, "ca.pwd");
                if (File.Exists(passwordPath))
                {
                    password = await File.ReadAllTextAsync(passwordPath, ct);
                }
                else
                {
                    throw new InvalidOperationException(
                        "CA key password not found. Set CaKeyPassword in configuration.");
                }
            }

            // Parse certificate
            using var cert = X509Certificate2.CreateFromPem(certPem);

            // Parse encrypted private key and combine with certificate
            using var rsa = RSA.Create();
            rsa.ImportFromEncryptedPem(keyPem, password);

            // Create certificate with private key
            using var certWithKey = cert.CopyWithPrivateKey(rsa);

            // Export and reimport to get a certificate that can be used for signing
            // This is necessary because CopyWithPrivateKey doesn't always result in a usable key
            var pfxBytes = certWithKey.Export(X509ContentType.Pfx, password);
            return new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.Exportable);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> GetCertificatePemAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_certPath))
            {
                throw new InvalidOperationException("CA certificate not found");
            }

            return await File.ReadAllTextAsync(_certPath, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string GenerateSecurePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }
}
