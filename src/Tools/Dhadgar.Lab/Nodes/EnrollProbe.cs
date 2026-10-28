using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// Builds the product's <c>EnrollNodeRequest</c> for the lab's agent members. The plain token is read from the vault by reference.
/// The probe key is an ECDSA P-256 key pair: the public half goes in the request as base64 SubjectPublicKeyInfo (the form
/// Nodes' <c>ParseAgentPublicKey</c> accepts); the private half is kept in the vault as base64 PKCS#8 under a <c>key-N</c> reference.
/// </summary>
public static partial class EnrollProbe
{
    /// <summary>
    /// The hostname a probe uses when none is given.
    /// </summary>
    public const string DefaultHostname = "meridian-lab-probe";

    /// <summary>
    /// The agent version the probe reports.
    /// </summary>
    public const string AgentVersion = "meridian-lab";

    [GeneratedRegex("^[a-z0-9-]{1,48}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnamePattern();

    /// <summary>
    /// Builds the request with the default hostname and a new key. The new key's reference is returned in <paramref name="keyRef"/>.
    /// </summary>
    public static Result<EnrollNodeRequest> BuildRequest(SecretVault vault, string tokenRef, string platform, out string keyRef)
    {
        return BuildRequest(vault, tokenRef, platform, DefaultHostname, null, out keyRef);
    }

    /// <summary>
    /// Builds the request. A non-null <paramref name="existingKeyRef"/> reuses a key the vault holds (for a retry); otherwise a new key is generated.
    /// </summary>
    public static Result<EnrollNodeRequest> BuildRequest(
        SecretVault vault,
        string tokenRef,
        string platform,
        string hostname,
        string? existingKeyRef,
        out string keyRef)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrEmpty(tokenRef);
        ArgumentException.ThrowIfNullOrEmpty(platform);
        ArgumentException.ThrowIfNullOrEmpty(hostname);

        keyRef = string.Empty;

        if (platform != "linux" && platform != "windows")
        {
            return Result<EnrollNodeRequest>.Failure("[Lab.InvalidArgument] platform must be windows or linux");
        }

        if (!HostnamePattern().IsMatch(hostname))
        {
            return Result<EnrollNodeRequest>.Failure("[Lab.InvalidArgument] hostname must match ^[a-z0-9-]{1,48}$");
        }

        Result<string> token = vault.Use(tokenRef, span => new string(span));
        if (token.IsFailure)
        {
            return Result<EnrollNodeRequest>.Failure(token.Error!);
        }

        Result<(string PublicKey, string KeyRef)> key = existingKeyRef is null
            ? CreateKey(vault)
            : ReuseKey(vault, existingKeyRef);
        if (key.IsFailure)
        {
            return Result<EnrollNodeRequest>.Failure(key.Error!);
        }

        keyRef = key.Value.KeyRef;

        var hardware = new HardwareInventoryRequest(
            Hostname: hostname,
            OsVersion: platform == "windows" ? "Windows Server 2022 (lab)" : "Ubuntu 24.04 (lab)",
            CpuCores: 4,
            MemoryBytes: 8L * 1024 * 1024 * 1024,
            DiskBytes: 128L * 1024 * 1024 * 1024,
            NetworkInterfaces: null);

        var request = new EnrollNodeRequest(
            Token: token.Value!,
            Platform: platform,
            Hardware: hardware,
            PublicKey: key.Value.PublicKey,
            AgentVersion: AgentVersion);
        return Result<EnrollNodeRequest>.Success(request);
    }

    private static Result<(string PublicKey, string KeyRef)> CreateKey(SecretVault vault)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = ecdsa.ExportSubjectPublicKeyInfo();
        byte[] pkcs8 = ecdsa.ExportPkcs8PrivateKey();
        char[]? chars = null;
        try
        {
            string publicKey = Convert.ToBase64String(spki);
            chars = Convert.ToBase64String(pkcs8).ToCharArray();
            string keyRef = vault.Add(SecretKind.PrivateKey, chars);
            return Result<(string, string)>.Success((publicKey, keyRef));
        }
        finally
        {
            Array.Clear(pkcs8);
            Array.Clear(spki);
            if (chars is not null)
            {
                Array.Clear(chars);
            }
        }
    }

    private static Result<(string PublicKey, string KeyRef)> ReuseKey(SecretVault vault, string existingKeyRef)
    {
        Result<string?> publicKey = vault.Use(existingKeyRef, span => DerivePublicKey(span));
        if (publicKey.IsFailure)
        {
            return Result<(string, string)>.Failure(publicKey.Error!);
        }

        string? derived = publicKey.Value;
        if (derived is null)
        {
            return Result<(string, string)>.Failure("[Lab.InvalidArgument] key reference does not hold a probe key");
        }

        return Result<(string, string)>.Success((derived, existingKeyRef));
    }

    private static string? DerivePublicKey(ReadOnlySpan<char> privateKeyBase64)
    {
        byte[]? pkcs8 = null;
        try
        {
            pkcs8 = Convert.FromBase64String(new string(privateKeyBase64));
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            if (pkcs8 is not null)
            {
                Array.Clear(pkcs8);
            }
        }
    }
}
