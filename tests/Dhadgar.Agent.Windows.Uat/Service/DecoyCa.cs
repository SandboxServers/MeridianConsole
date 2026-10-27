using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Service;

/// <summary>
/// A keyless decoy agent CA for service mode (D-S219, D-S222 (A)). The agent must remove any agent CA it finds in
/// <c>LocalMachine\Root</c>; U3 checks that it did. The decoy has no private key, so it cannot sign anything, and it
/// is removed at cleanup like every other <c>Root</c> entry the run adds.
/// </summary>
internal static class DecoyCa
{
    private const string LocalMachine = "LocalMachine";
    private const string RootStore = "Root";
    private const int LifetimeHours = 2;

    /// <summary>
    /// Creates the self-signed decoy, appends its <c>certificate</c> entry, then adds only the public certificate to
    /// <c>LocalMachine\Root</c>. Returns the SHA-1 thumbprint.
    /// </summary>
    public static Result<string> Plant(string runId, CleanupManifest manifest, IMachineActions machine)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(machine);

        byte[] raw;
        string thumbprint;
        try
        {
            (raw, thumbprint) = CreateDecoyBytes(runId);
        }
        catch (CryptographicException ex)
        {
            return Result<string>.Failure($"[DecoyCa.Create] {ex.GetType().Name}: {ex.Message}");
        }

        var recorded = manifest.Append(new CertificateEntry(LocalMachine, RootStore, thumbprint));
        if (recorded.IsFailure)
        {
            return Result<string>.Failure($"[DecoyCa.Manifest] {recorded.Error}");
        }

        using var publicOnly = X509CertificateLoader.LoadCertificate(raw);
        var added = machine.AddCertificate(LocalMachine, RootStore, publicOnly);
        return added.IsFailure
            ? Result<string>.Failure($"[DecoyCa.Root] {added.Error}")
            : Result<string>.Success(thumbprint);
    }

    /// <summary>Builds the decoy and returns its DER bytes and SHA-1 thumbprint. The key and certificate are disposed here.</summary>
    private static (byte[] Raw, string Thumbprint) CreateDecoyBytes(string runId)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN=Meridian Console Agent CA, O=UAT decoy {runId}",
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));

        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(LifetimeHours));
        return (cert.RawData, cert.Thumbprint);
    }
}
