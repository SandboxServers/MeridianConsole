using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Agents;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Windows;

/// <summary>
/// Reads certificate metadata from the stores the agent uses (<c>machine_my</c>, <c>machine_root</c>, <c>agent_ca</c>,
/// <c>current_user_my</c>). Read-only and metadata only: no certificate, public key blob or private key leaves this type.
/// A store that does not exist reads as an empty list.
/// </summary>
public static class CertStoreReader
{
    /// <summary>
    /// The store names accepted by <see cref="List"/>.
    /// </summary>
    public static IReadOnlyList<string> StoreNames { get; } = ["machine_my", "machine_root", "agent_ca", "current_user_my"];

    /// <summary>
    /// Lists the metadata of every certificate in <paramref name="store"/>.
    /// </summary>
    /// <param name="store">One of <see cref="StoreNames"/>.</param>
    /// <returns>The certificates, or a <c>[Lab.*]</c> failure.</returns>
    public static Result<IReadOnlyList<CertInfo>> List(string store)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result<IReadOnlyList<CertInfo>>.Failure("[Lab.Unavailable] certificate stores are read on Windows only");
        }

        return store switch
        {
            "machine_my" => ListStore("My", StoreLocation.LocalMachine),
            "machine_root" => ListStore("Root", StoreLocation.LocalMachine),
            "agent_ca" => ListStore(AgentWindowsFootprint.CaStoreName, StoreLocation.LocalMachine),
            "current_user_my" => ListStore("My", StoreLocation.CurrentUser),
            _ => Result<IReadOnlyList<CertInfo>>.Failure("[Lab.InvalidArgument] unknown certificate store"),
        };
    }

    private static Result<IReadOnlyList<CertInfo>> ListStore(string name, StoreLocation location)
    {
        try
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            X509Certificate2Collection collection = store.Certificates;

            var certs = new List<CertInfo>(collection.Count);
            foreach (var cert in collection)
            {
                using (cert)
                {
                    certs.Add(ToInfo(cert));
                }
            }

            return Result<IReadOnlyList<CertInfo>>.Success(certs);
        }
        catch (CryptographicException)
        {
            // OpenExistingOnly reports a missing store this way: it reads as empty.
            return Result<IReadOnlyList<CertInfo>>.Success(Array.Empty<CertInfo>());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return Result<IReadOnlyList<CertInfo>>.Failure("[Lab.Unavailable] certificate store read denied; use a member");
        }
    }

    private static CertInfo ToInfo(X509Certificate2 cert)
    {
        return new CertInfo(
            Thumbprint: cert.Thumbprint ?? string.Empty,
            Subject: cert.Subject,
            Issuer: cert.Issuer,
            Serial: cert.SerialNumber ?? string.Empty,
            NotBefore: new DateTimeOffset(cert.NotBefore),
            NotAfter: new DateTimeOffset(cert.NotAfter),
            HasPrivateKey: cert.HasPrivateKey);
    }
}
