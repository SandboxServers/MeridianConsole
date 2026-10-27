using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Service;

/// <summary>
/// What the run created for the Gateway's TLS: the PFX path (the Gateway reads it), the PFX password (for the Gateway
/// environment only) and the SHA-1 thumbprint of the public leaf that the run added to <c>LocalMachine\Root</c>.
/// <see cref="ToString"/> prints the path and the thumbprint only (R-LOG).
/// </summary>
internal sealed record UatTlsMaterial(string PfxPath, string Password, string ThumbprintSha1)
{
    /// <inheritdoc />
    public override string ToString() => $"UatTlsMaterial {{ PfxPath = {PfxPath}, ThumbprintSha1 = {ThumbprintSha1} }}";
}

/// <summary>
/// The run-scoped <c>CN=localhost</c> TLS leaf for service and MSI modes (F14, D-S222 (A)). A LocalSystem service
/// trusts only <c>LocalMachine</c> roots, so the leaf is added to <c>LocalMachine\Root</c> (public part only) and
/// removed at cleanup. The PFX with its private key stays under the run folder with an owner-only ACL.
/// </summary>
internal static class UatTlsCertificate
{
    /// <summary>File name of the PFX under <see cref="RunPaths.Tls"/>.</summary>
    public const string PfxFileName = "gateway.pfx";

    private const string LocalMachine = "LocalMachine";
    private const string RootStore = "Root";
    private const string SystemSid = "S-1-5-18";
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private const int PasswordBytes = 32;

    /// <summary>
    /// Creates the leaf, writes the PFX, and adds the public leaf to <c>LocalMachine\Root</c>. Each manifest entry is
    /// appended before the change it describes (the directory and file before the write, the certificate before the add).
    /// </summary>
    public static Result<UatTlsMaterial> Create(
        RunPaths paths,
        string runId,
        CleanupManifest manifest,
        Redactor redactor,
        IMachineActions machine)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(machine);

        var pfxPath = Path.Combine(paths.Tls, PfxFileName);
        var validated = paths.Validate(pfxPath);
        if (validated.IsFailure)
        {
            return Result<UatTlsMaterial>.Failure($"[UatTls.Path] {validated.Error}");
        }

        var passwordChars = Convert.ToHexString(RandomNumberGenerator.GetBytes(PasswordBytes)).ToCharArray();
        byte[]? pfxBytes = null;
        try
        {
            var password = new string(passwordChars);
            redactor.AddSecret(password, SecretKind.Password);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = DateTimeOffset.UtcNow;
            var request = new CertificateRequest(
                $"CN=localhost, O=Meridian Console UAT {runId}",
                key,
                HashAlgorithmName.SHA256);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid(ServerAuthOid, "serverAuth")],
                critical: false));

            using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(2));
            pfxBytes = cert.Export(X509ContentType.Pkcs12, password);
            var thumbprint = cert.Thumbprint;

            var directoryExisted = Directory.Exists(paths.Tls);
            var tlsDirectory = paths.Validate(paths.Tls);
            if (tlsDirectory.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure($"[UatTls.Path] {tlsDirectory.Error}");
            }

            var directoryRecorded = manifest.Append(new DirectoryEntry(paths.Tls, directoryExisted));
            if (directoryRecorded.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure($"[UatTls.Manifest] {directoryRecorded.Error}");
            }

            var fileRecorded = manifest.Append(new FileEntry(pfxPath));
            if (fileRecorded.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure($"[UatTls.Manifest] {fileRecorded.Error}");
            }

            Directory.CreateDirectory(paths.Tls);
            File.WriteAllBytes(pfxPath, pfxBytes);
            var acl = WriteOwnerOnlyAcl(pfxPath);
            if (acl.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure(acl.Error);
            }

            var certificateRecorded = manifest.Append(new CertificateEntry(LocalMachine, RootStore, thumbprint));
            if (certificateRecorded.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure($"[UatTls.Manifest] {certificateRecorded.Error}");
            }

            using var publicLeaf = X509CertificateLoader.LoadCertificate(cert.RawData);
            var added = machine.AddCertificate(LocalMachine, RootStore, publicLeaf);
            if (added.IsFailure)
            {
                return Result<UatTlsMaterial>.Failure($"[UatTls.Root] {added.Error}");
            }

            return Result<UatTlsMaterial>.Success(new UatTlsMaterial(pfxPath, password, thumbprint));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Result<UatTlsMaterial>.Failure($"[UatTls.Create] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Array.Clear(passwordChars);
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }
    }

    /// <summary>Protected DACL: FullControl for the current user and SYSTEM only; inherited rights removed.</summary>
    private static Result WriteOwnerOnlyAcl(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User;
        if (user is null)
        {
            return Result.Failure("[UatTls.Acl] the current user SID is unavailable");
        }

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(SystemSid),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
        return Result.Success();
    }
}
