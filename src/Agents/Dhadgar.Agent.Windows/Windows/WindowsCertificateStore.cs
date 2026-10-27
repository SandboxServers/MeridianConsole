using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows implementation of certificate storage using the Windows Certificate Store.
/// Uses <c>My</c> for the agent client certificate and the custom machine store <see cref="CaStoreName"/>
/// for the agent CA certificate.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL:
/// - This class manages mTLS certificates for agent authentication
/// - Certificates are stored in machine stores by default (requires Administrator)
/// - The agent CA is never added to LocalMachine\Root (D-S219): Root would make Windows trust anything it signs
/// - Private keys are PKCS#8 DER, and are deleted from disk when the certificate they belong to is replaced (A2)
/// - Certificate and key sizes are validated to prevent memory exhaustion
/// - Only partial thumbprints are logged to avoid leaking sensitive data
///
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCertificateStore : ICertificateStore, IDisposable
{
    private readonly ILogger<WindowsCertificateStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly StoreLocation _location;
    private readonly object _storeLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// Maximum allowed size for certificate or key data (16KB).
    /// Prevents memory exhaustion from malicious or malformed input.
    /// </summary>
    internal const int MaxCertificateSize = 16 * 1024;

    /// <summary>
    /// Name of the custom store that holds the agent CA certificate (D-S219). Every store operation on the CA uses it.
    /// </summary>
    internal const string CaStoreName = "MeridianConsoleAgent";

    /// <summary>
    /// Friendly name used to identify Meridian Console Agent certificates.
    /// </summary>
    private const string CertificateFriendlyName = "Meridian Console Agent";

    /// <summary>
    /// Number of random bytes behind the throwaway PFX password used during the key import round trip.
    /// </summary>
    private const int PfxPasswordBytes = 32;

    /// <summary>
    /// Creates the store. <paramref name="location"/> is <see cref="StoreLocation.LocalMachine"/> in production;
    /// tests use <see cref="StoreLocation.CurrentUser"/> so they do not need Administrator.
    /// </summary>
    public WindowsCertificateStore(
        ILogger<WindowsCertificateStore> logger,
        TimeProvider? timeProvider = null,
        StoreLocation location = StoreLocation.LocalMachine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _location = location;
    }

    /// <inheritdoc />
    public X509Certificate2? GetClientCertificate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_storeLock)
        {
            using var store = new X509Store(StoreName.My, _location);

            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                var now = _timeProvider.GetUtcNow();
                X509Certificate2? bestCert = null;

                foreach (var cert in store.Certificates)
                {
                    // Filter: must have private key, and be an agent client certificate valid now (Nodes profile)
                    if (!cert.HasPrivateKey || !AgentCertificateMatcher.IsAgentClientCertificate(cert, now))
                    {
                        cert.Dispose();
                        continue;
                    }

                    // Select the certificate with the latest expiration (newest valid cert)
                    if (bestCert is null || cert.NotAfter > bestCert.NotAfter)
                    {
                        bestCert?.Dispose();
                        bestCert = cert;
                    }
                    else
                    {
                        cert.Dispose();
                    }
                }

                if (bestCert is not null)
                {
                    _logger.LogDebug(
                        "Found client certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                        GetPartialThumbprint(bestCert.Thumbprint),
                        bestCert.NotAfter.ToUniversalTime());
                }
                else
                {
                    _logger.LogDebug("No valid client certificate found in store");
                }

                return bestCert;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to access certificate store {Location}\\My", _location);
                return null;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The new certificate is added before any older agent certificate is removed, so a crash part-way leaves two
    /// certificates rather than none. Each removed certificate's private key is deleted first (A2).
    /// </remarks>
    public Task StoreCertificateAsync(
        X509Certificate2 certificate,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(privateKey);

        // SECURITY: Validate sizes to prevent memory exhaustion
        if (privateKey.Length > MaxCertificateSize)
        {
            throw new ArgumentException(
                $"Private key exceeds maximum allowed size of {MaxCertificateSize} bytes",
                nameof(privateKey));
        }

        var rawData = certificate.RawData;
        if (rawData.Length > MaxCertificateSize)
        {
            throw new ArgumentException(
                $"Certificate exceeds maximum allowed size of {MaxCertificateSize} bytes",
                nameof(certificate));
        }

        // Import the PKCS#8 key and combine it with the certificate
        using var certWithKey = ImportPkcs8Certificate(certificate, privateKey, _location);

        lock (_storeLock)
        {
            using var store = new X509Store(StoreName.My, _location);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                // Add first, so a crash leaves two certificates rather than none
                store.Add(certWithKey);

                // Set friendly name after adding (Windows-specific)
                SetFriendlyName(store, certWithKey.Thumbprint, CertificateFriendlyName);

                // Then remove the other agent certificates, whatever their validity time
                var newThumbprint = certWithKey.Thumbprint;
                var removed = RemoveCertificates(
                    store,
                    cert => AgentCertificateMatcher.IsAgentClientIdentity(cert)
                        && !string.Equals(cert.Thumbprint, newThumbprint, StringComparison.OrdinalIgnoreCase),
                    deleteKeys: true);

                if (removed > 0)
                {
                    _logger.LogInformation("Replaced {Count} older agent certificate(s) in store", removed);
                }

                _logger.LogInformation(
                    "Stored client certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                    GetPartialThumbprint(certWithKey.Thumbprint),
                    certWithKey.NotAfter.ToUniversalTime());
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to store client certificate in {Location}\\My", _location);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_storeLock)
        {
            using var store = new X509Store(StoreName.My, _location);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                var removed = RemoveCertificates(store, AgentCertificateMatcher.IsAgentClientIdentity, deleteKeys: false);

                if (removed > 0)
                {
                    _logger.LogInformation("Removed {Count} client certificate(s) from store", removed);
                }
                else
                {
                    _logger.LogDebug("No client certificates found to remove");
                }
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to remove client certificate from {Location}\\My", _location);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public X509Certificate2? GetCaCertificate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_storeLock)
        {
            using var store = new X509Store(CaStoreName, _location);

            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                var now = _timeProvider.GetUtcNow();
                X509Certificate2? bestCert = null;

                foreach (var cert in store.Certificates)
                {
                    if (!AgentCertificateMatcher.IsAgentCa(cert) || !AgentCertificateMatcher.IsWithinValidity(cert, now))
                    {
                        cert.Dispose();
                        continue;
                    }

                    // Select the certificate with the latest expiration (newest valid cert)
                    if (bestCert is null || cert.NotAfter > bestCert.NotAfter)
                    {
                        bestCert?.Dispose();
                        bestCert = cert;
                    }
                    else
                    {
                        cert.Dispose();
                    }
                }

                if (bestCert is not null)
                {
                    _logger.LogDebug(
                        "Found CA certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                        GetPartialThumbprint(bestCert.Thumbprint),
                        bestCert.NotAfter.ToUniversalTime());
                }
                else
                {
                    _logger.LogDebug("No valid CA certificate found in store");
                }

                return bestCert;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to access certificate store {Location}\\{Store}", _location, CaStoreName);
                return null;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The CA goes to the custom store <see cref="CaStoreName"/>, never to LocalMachine\Root (D-S219). Any copy an
    /// earlier version put in Root is removed first.
    /// </remarks>
    public Task StoreCaCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(certificate);

        // SECURITY: Validate size to prevent memory exhaustion
        var rawData = certificate.RawData;
        if (rawData.Length > MaxCertificateSize)
        {
            throw new ArgumentException(
                $"Certificate exceeds maximum allowed size of {MaxCertificateSize} bytes",
                nameof(certificate));
        }

        lock (_storeLock)
        {
            RemoveAgentCaFromRoot();

            using var store = new X509Store(CaStoreName, _location);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                // Remove any existing agent CA certificates before adding the new one
                RemoveCertificates(store, AgentCertificateMatcher.IsAgentCa, deleteKeys: false);

                store.Add(certificate);

                // Set friendly name after adding
                SetFriendlyName(store, certificate.Thumbprint, CertificateFriendlyName + " CA");

                _logger.LogInformation(
                    "Stored CA certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                    GetPartialThumbprint(certificate.Thumbprint),
                    certificate.NotAfter.ToUniversalTime());
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to store CA certificate in {Location}\\{Store}", _location, CaStoreName);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool NeedsRenewal(int thresholdDays)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (thresholdDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdDays),
                thresholdDays,
                "Threshold days cannot be negative");
        }

        using var certificate = GetClientCertificate();

        if (certificate is null)
        {
            _logger.LogDebug("No client certificate found, renewal needed");
            return true;
        }

        var renewalThreshold = _timeProvider.GetUtcNow().UtcDateTime.AddDays(thresholdDays);
        var notAfterUtc = certificate.NotAfter.ToUniversalTime();
        var needsRenewal = notAfterUtc <= renewalThreshold;

        if (needsRenewal)
        {
            _logger.LogInformation(
                "Client certificate expires {NotAfter:O}, within {Days} day threshold - renewal needed",
                notAfterUtc,
                thresholdDays);
        }
        else
        {
            _logger.LogDebug(
                "Client certificate expires {NotAfter:O}, outside {Days} day threshold - no renewal needed",
                notAfterUtc,
                thresholdDays);
        }

        return needsRenewal;
    }

    /// <summary>
    /// Combines a certificate with its PKCS#8 private key and re-imports it with persisted key storage.
    /// </summary>
    /// <remarks>
    /// SECURITY: The key is tried as ECDSA first, then RSA. The PFX round trip is required because the Windows store
    /// needs the key persisted in a key container; the PFX bytes and the random password are cleared before returning.
    /// </remarks>
    private static X509Certificate2 ImportPkcs8Certificate(
        X509Certificate2 certificate,
        byte[] pkcs8,
        StoreLocation location)
    {
        using var ecdsa = ECDsa.Create();
        if (TryImportPkcs8(ecdsa, pkcs8))
        {
            using var withEcdsaKey = certificate.CopyWithPrivateKey(ecdsa);
            return ExportAndReimportCertificate(withEcdsaKey, location);
        }

        using var rsa = RSA.Create();
        if (TryImportPkcs8(rsa, pkcs8))
        {
            using var withRsaKey = certificate.CopyWithPrivateKey(rsa);
            return ExportAndReimportCertificate(withRsaKey, location);
        }

        throw new CryptographicException("Failed to import private key - unsupported key type or invalid format");
    }

    private static bool TryImportPkcs8(ECDsa key, byte[] pkcs8)
    {
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryImportPkcs8(RSA key, byte[] pkcs8)
    {
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Exports the certificate with its key to PFX under a random password and re-imports it with persisted key storage.
    /// </summary>
    /// <remarks>
    /// SECURITY: The Exportable flag is left off on the re-import. The key is protected by Windows DPAPI and
    /// machine-level ACLs. The PFX bytes and the password are cleared in <c>finally</c>.
    /// </remarks>
    private static X509Certificate2 ExportAndReimportCertificate(X509Certificate2 certWithKey, StoreLocation location)
    {
        var password = NewPfxPassword();
        byte[]? pfxBytes = null;
        try
        {
            // SecureString keeps the password out of managed string memory; the runtime exposes no char[] export overload
            using var securePassword = new SecureString();
            foreach (var character in password)
            {
                securePassword.AppendChar(character);
            }

            securePassword.MakeReadOnly();
            pfxBytes = certWithKey.Export(X509ContentType.Pfx, securePassword);

            var keySet = location == StoreLocation.CurrentUser
                ? X509KeyStorageFlags.UserKeySet
                : X509KeyStorageFlags.MachineKeySet;

            return new X509Certificate2(
                pfxBytes,
                password.AsSpan(),
                keySet | X509KeyStorageFlags.PersistKeySet);
        }
        finally
        {
            // SECURITY: Clear the PFX bytes and the password from memory
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }

            Array.Clear(password);
        }
    }

    /// <summary>
    /// Generates a random PFX password of <see cref="PfxPasswordBytes"/> bytes' worth of entropy, as characters
    /// so the caller can clear it.
    /// </summary>
    private static char[] NewPfxPassword()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        var randomBytes = RandomNumberGenerator.GetBytes(PfxPasswordBytes);
        try
        {
            var password = new char[PfxPasswordBytes];
            for (var i = 0; i < password.Length; i++)
            {
                // 64-character alphabet, so each byte's low six bits pick one character
                password[i] = alphabet[randomBytes[i] & 0x3F];
            }

            return password;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    /// <summary>
    /// Removes the certificates in the store that match <paramref name="matches"/>.
    /// </summary>
    /// <param name="store">An open store with write access.</param>
    /// <param name="matches">Selects the certificates to remove.</param>
    /// <param name="deleteKeys">
    /// When true, each certificate's persisted private key is deleted before the certificate is removed (A2).
    /// A key that cannot be deleted is logged and does not stop the removal.
    /// </param>
    /// <returns>The number of certificates removed.</returns>
    private int RemoveCertificates(X509Store store, Func<X509Certificate2, bool> matches, bool deleteKeys)
    {
        var removed = 0;
        var certsToRemove = new List<X509Certificate2>();

        try
        {
            // store.Certificates creates new X509Certificate2 objects that must be disposed.
            // Disposing only releases the managed wrapper; it does NOT remove certs from the store.
            foreach (var cert in store.Certificates)
            {
                if (matches(cert))
                {
                    certsToRemove.Add(cert);
                }
                else
                {
                    // Dispose non-matching certificates to avoid memory leaks
                    cert.Dispose();
                }
            }

            foreach (var cert in certsToRemove)
            {
                try
                {
                    if (deleteKeys)
                    {
                        DeletePrivateKey(cert);
                    }

                    store.Remove(cert);
                    removed++;

                    _logger.LogDebug(
                        "Removed certificate with thumbprint {Thumbprint} from store",
                        GetPartialThumbprint(cert.Thumbprint));
                }
                catch (CryptographicException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to remove certificate with thumbprint {Thumbprint}",
                        GetPartialThumbprint(cert.Thumbprint));
                }
            }
        }
        finally
        {
            foreach (var cert in certsToRemove)
            {
                cert.Dispose();
            }
        }

        return removed;
    }

    /// <summary>
    /// Deletes the persisted private key of a certificate that is being replaced (A2). Without this, every renewal
    /// leaves the key of a revoked certificate in the machine key container.
    /// </summary>
    /// <remarks>
    /// A failure is logged at Warning with the thumbprint and does not fail the store operation.
    /// </remarks>
    private void DeletePrivateKey(X509Certificate2 certificate)
    {
        try
        {
            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is ECDsaCng ecdsaCng)
            {
                ecdsaCng.Key.Delete();
                return;
            }

            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is RSACng rsaCng)
            {
                rsaCng.Key.Delete();
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete the private key of certificate with thumbprint {Thumbprint}",
                GetPartialThumbprint(certificate.Thumbprint));
        }
    }

    /// <summary>
    /// Removes agent CA certificates that an earlier version stored in LocalMachine\Root (D-S219).
    /// </summary>
    /// <remarks>
    /// Runs once per CA store operation; it logs what it removed. A failure (for example no write access to Root)
    /// is logged at Warning and does not stop the CA from being stored in its own store.
    /// </remarks>
    private void RemoveAgentCaFromRoot()
    {
        try
        {
            using var root = new X509Store(StoreName.Root, _location);
            root.Open(OpenFlags.ReadWrite);

            var removed = RemoveCertificates(root, AgentCertificateMatcher.IsAgentCa, deleteKeys: false);

            if (removed > 0)
            {
                _logger.LogWarning(
                    "Removed {Count} agent CA certificate(s) from {Location}\\Root; the agent CA is kept in {Store}",
                    removed,
                    _location,
                    CaStoreName);
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Could not check {Location}\\Root for an earlier agent CA", _location);
        }
    }

    /// <summary>
    /// Sets the friendly name on a certificate in the store.
    /// </summary>
    /// <remarks>
    /// We must dispose all certificates from store.Certificates except the matched one,
    /// as it needs to remain valid while we set FriendlyName.
    /// </remarks>
    private static void SetFriendlyName(X509Store store, string thumbprint, string friendlyName)
    {
        foreach (var cert in store.Certificates)
        {
            if (string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                // Found matching certificate - set friendly name and return
                // Don't dispose this cert as we just modified it
                cert.FriendlyName = friendlyName;
                return;
            }

            // Not a match - dispose to prevent handle leak
            cert.Dispose();
        }
    }

    /// <summary>
    /// Gets a partial thumbprint for logging (first 8 characters + "...").
    /// SECURITY: Avoids logging full thumbprints which could aid attackers.
    /// </summary>
    private static string GetPartialThumbprint(string thumbprint)
    {
        if (string.IsNullOrEmpty(thumbprint))
        {
            return "(empty)";
        }

        return thumbprint.Length > 8
            ? thumbprint[..8] + "..."
            : thumbprint;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // SECURITY: Set _disposed first to prevent any new operations
        _disposed = true;

        // No managed resources to dispose - X509Store instances are created
        // and disposed within each method call
    }
}
