using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Supplies the client certificate used for mTLS handshakes. The certificate is loaded lazily
/// from <see cref="ICertificateStore"/> and cached until <see cref="Invalidate"/> is called.
/// </summary>
public interface IClientCertificateProvider
{
    /// <summary>
    /// The current client certificate with its private key, or null when none is stored.
    /// The provider owns the returned instance; callers must not dispose it.
    /// </summary>
    X509Certificate2? Current { get; }

    /// <summary>
    /// Discards the cached certificate so the next <see cref="Current"/> reads the store again.
    /// </summary>
    void Invalidate();
}
