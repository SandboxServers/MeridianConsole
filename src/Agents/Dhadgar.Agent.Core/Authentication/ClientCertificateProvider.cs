using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Caches the client certificate read from <see cref="ICertificateStore"/> and hands it to each
/// mTLS handshake. Enrollment and renewal call <see cref="Invalidate"/> after storing new material.
/// </summary>
public sealed class ClientCertificateProvider : IClientCertificateProvider, IDisposable
{
    private readonly ICertificateStore _certificateStore;
    private readonly object _lock = new();
    private X509Certificate2? _certificate;

    // A handshake may hold the instance returned by Current after Invalidate runs, and SslStream
    // uses it after the selection callback returns. The retired instance is disposed one
    // generation later (on the next Invalidate) or on Dispose, never on the same call.
    private X509Certificate2? _retired;
    private bool _loaded;
    private bool _disposed;

    public ClientCertificateProvider(ICertificateStore certificateStore)
    {
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
    }

    /// <inheritdoc />
    public X509Certificate2? Current
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_loaded)
                {
                    _certificate = _certificateStore.GetClientCertificate();
                    _loaded = true;
                }

                return _certificate;
            }
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_lock)
        {
            _retired?.Dispose();
            _retired = _certificate;
            _certificate = null;
            _loaded = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _certificate?.Dispose();
            _certificate = null;
            _retired?.Dispose();
            _retired = null;
            _loaded = false;
            _disposed = true;
        }
    }
}
