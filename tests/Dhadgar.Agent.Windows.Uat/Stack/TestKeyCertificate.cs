using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// Builds the client certificate the UAT uses for S-negotiate and S-hub-ws from the PEM Nodes issued (P19).
/// The PFX round trip uses a random password and a user key set, because SChannel cannot use an ephemeral key
/// for a client certificate. The PFX bytes and password are zeroed before return.
/// </summary>
internal static class TestKeyCertificate
{
    /// <summary>Combines an issued certificate PEM with the test key. The caller disposes the returned certificate.</summary>
    public static Result<X509Certificate2> FromPem(ECDsa key, string certificatePem)
    {
        byte[]? pfx = null;
        byte[]? passwordBytes = null;
        try
        {
            using var leaf = X509Certificate2.CreateFromPem(certificatePem);
            using var withKey = leaf.CopyWithPrivateKey(key);

            passwordBytes = RandomNumberGenerator.GetBytes(32);
            var password = Convert.ToHexString(passwordBytes).ToLowerInvariant();
            pfx = withKey.Export(X509ContentType.Pfx, password);

            var loaded = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet);
            return Result<X509Certificate2>.Success(loaded);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or InvalidOperationException)
        {
            return Result<X509Certificate2>.Failure($"client certificate build failed: {ex.GetType().Name}");
        }
        finally
        {
            if (pfx is not null)
            {
                Array.Clear(pfx);
            }

            if (passwordBytes is not null)
            {
                Array.Clear(passwordBytes);
            }
        }
    }
}
