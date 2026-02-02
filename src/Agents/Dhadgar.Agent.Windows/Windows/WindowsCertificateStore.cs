using System.Buffers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dhadgar.Agent.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows implementation of certificate storage using the Windows Certificate Store.
/// Uses LocalMachine\My for client certificates and LocalMachine\Root for CA certificates.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL:
/// - This class manages mTLS certificates for agent authentication
/// - Certificates are stored in LocalMachine stores (requires Administrator)
/// - Private keys use PKCS#8 or PEM format
/// - Certificate and key sizes are validated to prevent memory exhaustion
/// - Only partial thumbprints are logged to avoid leaking sensitive data
///
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCertificateStore : ICertificateStore, IDisposable
{
    private readonly ILogger<WindowsCertificateStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _storeLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// Maximum allowed size for certificate or key data (16KB).
    /// Prevents memory exhaustion from malicious or malformed input.
    /// </summary>
    internal const int MaxCertificateSize = 16 * 1024;

    /// <summary>
    /// Friendly name used to identify Meridian Console Agent certificates.
    /// </summary>
    private const string CertificateFriendlyName = "Meridian Console Agent";

    /// <summary>
    /// Subject name for client certificates.
    /// </summary>
    private const string ClientSubjectName = "CN=dhadgar-agent";

    /// <summary>
    /// Subject name for CA certificates.
    /// </summary>
    private const string CaSubjectName = "CN=Meridian Console CA";

    public WindowsCertificateStore(
        ILogger<WindowsCertificateStore> logger,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public X509Certificate2? GetClientCertificate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_storeLock)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);

            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                X509Certificate2? bestCert = null;

                foreach (var cert in store.Certificates)
                {
                    // Filter: must have private key, be valid, and match our subject
                    if (!cert.HasPrivateKey)
                    {
                        cert.Dispose();
                        continue;
                    }

                    if (!IsValidCertificate(cert, now))
                    {
                        cert.Dispose();
                        continue;
                    }

                    if (!MatchesSubject(cert, ClientSubjectName))
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
                _logger.LogError(ex, "Failed to access certificate store LocalMachine\\My");
                return null;
            }
        }
    }

    /// <inheritdoc />
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

        // Import the private key and combine with certificate
        using var certWithKey = ImportCertificateWithKey(certificate, privateKey);

        lock (_storeLock)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                // Remove any existing certificates with the same subject to avoid duplicates
                RemoveExistingCertificates(store, ClientSubjectName);

                // Create a new certificate with the friendly name set
                // Note: FriendlyName can only be set on certificates in a store on Windows
                store.Add(certWithKey);

                // Set friendly name after adding (Windows-specific)
                SetFriendlyName(store, certWithKey.Thumbprint, CertificateFriendlyName);

                _logger.LogInformation(
                    "Stored client certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                    GetPartialThumbprint(certWithKey.Thumbprint),
                    certWithKey.NotAfter.ToUniversalTime());
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to store client certificate in LocalMachine\\My");
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
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                var removed = RemoveExistingCertificates(store, ClientSubjectName);

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
                _logger.LogError(ex, "Failed to remove client certificate from LocalMachine\\My");
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
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);

            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                X509Certificate2? bestCert = null;

                foreach (var cert in store.Certificates)
                {
                    if (!IsValidCertificate(cert, now))
                    {
                        cert.Dispose();
                        continue;
                    }

                    if (!MatchesSubject(cert, CaSubjectName))
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
                _logger.LogError(ex, "Failed to access certificate store LocalMachine\\Root");
                return null;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// CA5380 is suppressed because adding the control plane's CA certificate to the
    /// trusted root store is an intentional and necessary part of agent enrollment.
    /// The CA is controlled by the Meridian Console control plane and is required
    /// for mTLS authentication.
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
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);

            try
            {
                store.Open(OpenFlags.ReadWrite);

                // Remove any existing CA certificates with the same subject
                RemoveExistingCertificates(store, CaSubjectName);

                // CA5380: Adding to root store is intentional - this is the control plane's CA
                // required for mTLS authentication between agent and control plane
#pragma warning disable CA5380
                store.Add(certificate);
#pragma warning restore CA5380

                // Set friendly name after adding
                SetFriendlyName(store, certificate.Thumbprint, CertificateFriendlyName + " CA");

                _logger.LogInformation(
                    "Stored CA certificate with thumbprint {Thumbprint}, expires {NotAfter:O}",
                    GetPartialThumbprint(certificate.Thumbprint),
                    certificate.NotAfter.ToUniversalTime());
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to store CA certificate in LocalMachine\\Root");
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
    /// Imports a certificate with its private key from PEM or PKCS#8 format.
    /// </summary>
    /// <remarks>
    /// SECURITY: This method uses ArrayPool and Span to avoid creating immutable strings
    /// containing private key data. All temporary buffers are cleared before returning.
    /// </remarks>
    private static X509Certificate2 ImportCertificateWithKey(
        X509Certificate2 certificate,
        byte[] privateKeyBytes)
    {
        // SECURITY: Check for PEM format using byte comparison instead of string conversion
        // This avoids creating an immutable string containing private key data
        ReadOnlySpan<byte> pemHeader = "-----BEGIN"u8;
        var isPem = privateKeyBytes.AsSpan().IndexOf(pemHeader) >= 0;

        char[]? charBuffer = null;

        try
        {
            // For PEM parsing, we need characters but use a pooled buffer that can be cleared
            ReadOnlySpan<char> keyChars = ReadOnlySpan<char>.Empty;

            if (isPem)
            {
                // SECURITY: Use ArrayPool for char buffer so we can clear it after use
                charBuffer = ArrayPool<char>.Shared.Rent(privateKeyBytes.Length);
                var charCount = Encoding.UTF8.GetChars(privateKeyBytes, charBuffer);
                keyChars = charBuffer.AsSpan(0, charCount);
            }

            // Try RSA first, then ECDSA
            var certWithKey = TryImportWithRsaOrEcdsa(certificate, privateKeyBytes, keyChars, isPem);

            // Export and re-import to ensure the certificate is persisted properly
            // This is necessary for Windows certificate store operations
            return ExportAndReimportCertificate(certWithKey);
        }
        finally
        {
            // SECURITY: Clear and return the char buffer to the pool
            if (charBuffer is not null)
            {
                CryptographicOperations.ZeroMemory(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(charBuffer.AsSpan()));
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }
    }

    /// <summary>
    /// Tries to import a private key as RSA or ECDSA and combine with certificate.
    /// </summary>
    private static X509Certificate2 TryImportWithRsaOrEcdsa(
        X509Certificate2 certificate,
        byte[] privateKeyBytes,
        ReadOnlySpan<char> keyChars,
        bool isPem)
    {
        // Try RSA first
        var (rsaCert, rsaSuccess) = TryImportWithRsa(certificate, privateKeyBytes, keyChars, isPem);
        if (rsaSuccess)
        {
            return rsaCert!;
        }

        // Try ECDSA if RSA failed
        var (ecdsaCert, ecdsaSuccess) = TryImportWithEcdsa(certificate, privateKeyBytes, keyChars, isPem);
        if (ecdsaSuccess)
        {
            return ecdsaCert!;
        }

        throw new CryptographicException("Failed to import private key - unsupported key type or invalid format");
    }

    /// <summary>
    /// Exports the certificate to PFX and re-imports with proper storage flags.
    /// </summary>
    /// <remarks>
    /// SECURITY: The Exportable flag is intentional here - the Windows certificate
    /// store requires it to persist the private key properly for mTLS operations.
    /// The key is protected by Windows DPAPI and machine-level ACLs.
    /// </remarks>
    private static X509Certificate2 ExportAndReimportCertificate(X509Certificate2 certWithKey)
    {
        byte[]? pfxBytes = null;
        try
        {
            pfxBytes = certWithKey.Export(X509ContentType.Pfx);
            certWithKey.Dispose();

            var finalCert = new X509Certificate2(
                pfxBytes,
                (string?)null,
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.Exportable);

            return finalCert;
        }
        finally
        {
            // SECURITY: Clear the PFX bytes from memory
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }
    }

    /// <summary>
    /// Attempts to import a private key as RSA and combine with certificate.
    /// </summary>
    /// <param name="certificate">The certificate to combine with the key.</param>
    /// <param name="privateKeyBytes">The raw private key bytes (used for PKCS#8).</param>
    /// <param name="keyChars">The key as characters (used for PEM). Can be empty if not PEM.</param>
    /// <param name="isPem">True if the key is in PEM format.</param>
    /// <returns>A tuple containing the certificate with key (if successful) and a success flag.</returns>
    private static (X509Certificate2? Certificate, bool Success) TryImportWithRsa(
        X509Certificate2 certificate,
        byte[] privateKeyBytes,
        ReadOnlySpan<char> keyChars,
        bool isPem)
    {
        using var rsaKey = RSA.Create();

        try
        {
            if (isPem)
            {
                rsaKey.ImportFromPem(keyChars);
            }
            else
            {
                rsaKey.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            }

            return (certificate.CopyWithPrivateKey(rsaKey), true);
        }
        catch (CryptographicException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Attempts to import a private key as ECDSA and combine with certificate.
    /// </summary>
    /// <param name="certificate">The certificate to combine with the key.</param>
    /// <param name="privateKeyBytes">The raw private key bytes (used for PKCS#8).</param>
    /// <param name="keyChars">The key as characters (used for PEM). Can be empty if not PEM.</param>
    /// <param name="isPem">True if the key is in PEM format.</param>
    /// <returns>A tuple containing the certificate with key (if successful) and a success flag.</returns>
    private static (X509Certificate2? Certificate, bool Success) TryImportWithEcdsa(
        X509Certificate2 certificate,
        byte[] privateKeyBytes,
        ReadOnlySpan<char> keyChars,
        bool isPem)
    {
        using var ecdsaKey = ECDsa.Create();

        try
        {
            if (isPem)
            {
                ecdsaKey.ImportFromPem(keyChars);
            }
            else
            {
                ecdsaKey.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            }

            return (certificate.CopyWithPrivateKey(ecdsaKey), true);
        }
        catch (CryptographicException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Checks if a certificate is currently valid (not expired and not yet future).
    /// </summary>
    private static bool IsValidCertificate(X509Certificate2 certificate, DateTime utcNow)
    {
        var notBeforeUtc = certificate.NotBefore.ToUniversalTime();
        var notAfterUtc = certificate.NotAfter.ToUniversalTime();

        return notBeforeUtc <= utcNow && notAfterUtc > utcNow;
    }

    /// <summary>
    /// Checks if a certificate's subject matches the expected subject name.
    /// </summary>
    private static bool MatchesSubject(X509Certificate2 certificate, string expectedSubject)
    {
        // Use SimpleName to get just the CN value for comparison
        var simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var expectedCn = expectedSubject.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)
            ? expectedSubject[3..]
            : expectedSubject;

        return string.Equals(simpleName, expectedCn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes all certificates matching the given subject from the store.
    /// </summary>
    /// <returns>The number of certificates removed.</returns>
    private int RemoveExistingCertificates(X509Store store, string subjectName)
    {
        var removed = 0;
        var certsToRemove = new List<X509Certificate2>();

        try
        {
            // Note: We only track certificates that match our subject.
            // Non-matching certificates are not disposed here as they remain
            // in the store and may be used by other applications.
            foreach (var cert in store.Certificates)
            {
                if (MatchesSubject(cert, subjectName))
                {
                    certsToRemove.Add(cert);
                }
                // Non-matching certificates are intentionally not disposed here
                // as disposing them doesn't remove them from the store and could
                // cause issues if other code holds references to them.
            }

            foreach (var cert in certsToRemove)
            {
                try
                {
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
    /// Sets the friendly name on a certificate in the store.
    /// </summary>
    private static void SetFriendlyName(X509Store store, string thumbprint, string friendlyName)
    {
        foreach (var cert in store.Certificates)
        {
            try
            {
                if (string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    cert.FriendlyName = friendlyName;
                    break;
                }
            }
            finally
            {
                cert.Dispose();
            }
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
