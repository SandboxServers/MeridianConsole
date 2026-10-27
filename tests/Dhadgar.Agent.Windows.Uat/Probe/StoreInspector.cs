using System.Formats.Asn1;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Probe;

/// <summary>What the driver reads about a certificate's CNG key.</summary>
/// <param name="KeyName">The CNG key name.</param>
/// <param name="Provider">The CNG provider name.</param>
/// <param name="IsMachineKey">True when the key lives in the machine key store.</param>
/// <param name="CanSign">True when a sign and verify round trip over 32 random bytes succeeds.</param>
internal sealed record CngKeyInfo(string KeyName, string Provider, bool IsMachineKey, bool CanSign);

/// <summary>A certificate as the driver reads it from a store (validity ignored).</summary>
/// <param name="Subject">The subject name.</param>
/// <param name="Sha1Thumbprint">SHA-1 thumbprint, uppercase hex.</param>
/// <param name="Sha256Thumbprint">SHA-256 thumbprint, uppercase hex.</param>
/// <param name="SanUris">The URI entries of the subject alternative name.</param>
/// <param name="NotBefore">Start of validity (UTC).</param>
/// <param name="NotAfter">End of validity (UTC).</param>
/// <param name="HasPrivateKey">True when the certificate carries a private key.</param>
/// <param name="Key">The CNG key details when the key is an ECDSA CNG key; otherwise null.</param>
internal sealed record StoreCert(
    string Subject,
    string Sha1Thumbprint,
    string Sha256Thumbprint,
    IReadOnlyList<string> SanUris,
    DateTime NotBefore,
    DateTime NotAfter,
    bool HasPrivateKey,
    CngKeyInfo? Key);

/// <summary>The protected flag and the SIDs of the Allow ACEs on the state file (A1, D-S233).</summary>
/// <param name="IsProtected">True when inherited ACEs are blocked.</param>
/// <param name="AllowSids">The SID of every Allow ACE.</param>
internal sealed record StateFileAclInfo(bool IsProtected, IReadOnlyList<string> AllowSids);

/// <summary>Reads the agent's certificate stores and the state file ACL. Abstracted so legs run with a fake.</summary>
internal interface IStoreInspector
{
    /// <summary>Every <c>LocalMachine\My</c> certificate that is an agent client certificate (validity ignored).</summary>
    IReadOnlyList<StoreCert> AgentCertificatesInMy();

    /// <summary>Every CA certificate in the agent's CA store (<c>MeridianConsoleAgent</c>).</summary>
    IReadOnlyList<StoreCert> CaInAgentStore();

    /// <summary>Every agent CA certificate in <c>LocalMachine\Root</c> (must be empty after a clean run).</summary>
    IReadOnlyList<StoreCert> AgentCasInRoot();

    /// <summary>Reads the DACL of a file.</summary>
    Result<StateFileAclInfo> StateFileAcl(string path);
}

/// <summary>Windows implementation of <see cref="IStoreInspector"/>.</summary>
internal sealed class StoreInspector : IStoreInspector
{
    private const string SanOid = "2.5.29.17";
    private const int UriTagNumber = 6;

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> AgentCertificatesInMy()
    {
        return ReadStore(StoreName.My, StoreLocation.LocalMachine, IsAgentClient);
    }

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> CaInAgentStore()
    {
        return ReadStore(AgentWindowsFacts.CaStoreName, IsCa);
    }

    /// <inheritdoc />
    public IReadOnlyList<StoreCert> AgentCasInRoot()
    {
        return ReadStore(StoreName.Root, StoreLocation.LocalMachine, IsCa);
    }

    /// <inheritdoc />
    public Result<StateFileAclInfo> StateFileAcl(string path)
    {
        try
        {
            var security = new FileInfo(path).GetAccessControl();
            var sids = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(rule => rule.AccessControlType == AccessControlType.Allow)
                .Select(rule => rule.IdentityReference.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Result<StateFileAclInfo>.Success(new StateFileAclInfo(security.AreAccessRulesProtected, sids));
        }
        catch (IOException)
        {
            return Result<StateFileAclInfo>.Failure("[StateFile.Acl] state file ACL could not be read");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<StateFileAclInfo>.Failure("[StateFile.Acl] access denied reading the state file ACL");
        }
    }

    private static bool IsAgentClient(X509Certificate2 cert) => AgentCertificateMatcher.IsAgentClientIdentity(cert);

    private static bool IsCa(X509Certificate2 cert) => AgentCertificateMatcher.IsAgentCa(cert);

    private static IReadOnlyList<StoreCert> ReadStore(StoreName name, StoreLocation location, Func<X509Certificate2, bool> keep)
    {
        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly);
        return Collect(store, keep);
    }

    private static IReadOnlyList<StoreCert> ReadStore(string name, Func<X509Certificate2, bool> keep)
    {
        using var store = new X509Store(name, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        return Collect(store, keep);
    }

    private static IReadOnlyList<StoreCert> Collect(X509Store store, Func<X509Certificate2, bool> keep)
    {
        var found = new List<StoreCert>();
        foreach (var cert in store.Certificates)
        {
            using (cert)
            {
                if (keep(cert))
                {
                    found.Add(Describe(cert));
                }
            }
        }

        return found;
    }

    private static StoreCert Describe(X509Certificate2 cert)
    {
        var hasKey = cert.HasPrivateKey;
        return new StoreCert(
            cert.Subject,
            cert.Thumbprint,
            Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)),
            SanUris(cert),
            cert.NotBefore.ToUniversalTime(),
            cert.NotAfter.ToUniversalTime(),
            hasKey,
            hasKey ? DescribeKey(cert) : null);
    }

    private static CngKeyInfo? DescribeKey(X509Certificate2 cert)
    {
        using var ecdsa = cert.GetECDsaPrivateKey();
        if (ecdsa is not ECDsaCng cng)
        {
            return null;
        }

        var canSign = false;
        try
        {
            var data = RandomNumberGenerator.GetBytes(32);
            var signature = cng.SignData(data, HashAlgorithmName.SHA256);
            canSign = cng.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            canSign = false;
        }

        return new CngKeyInfo(cng.Key.KeyName ?? string.Empty, cng.Key.Provider?.Provider ?? string.Empty, cng.Key.IsMachineKey, canSign);
    }

    /// <summary>Reads the URI entries of the subject alternative name with a DER reader.</summary>
    private static IReadOnlyList<string> SanUris(X509Certificate2 cert)
    {
        var uris = new List<string>();
        foreach (var extension in cert.Extensions)
        {
            if (extension.Oid?.Value != SanOid)
            {
                continue;
            }

            var names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (names.HasData)
            {
                var tag = names.PeekTag();
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == UriTagNumber)
                {
                    uris.Add(names.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, UriTagNumber)));
                }
                else
                {
                    names.ReadEncodedValue();
                }
            }
        }

        return uris;
    }
}
