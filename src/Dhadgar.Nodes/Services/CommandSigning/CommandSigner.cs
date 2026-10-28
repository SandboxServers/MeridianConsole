using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services.CommandSigning;

/// <summary>
/// Singleton command signer. Holds one P-256 key for the lifetime of the process and disposes it on shutdown.
/// </summary>
public sealed class CommandSigner : ICommandSigner, IDisposable
{
    private const string DefaultKeyFileName = "command-signing-key.pem";
    private const string P256OidValue = "1.2.840.10045.3.1.7";

    private readonly CommandSigningOptions _signingOptions;
    private readonly NodesOptions _nodesOptions;
    private readonly ILogger<CommandSigner> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ECDsa? _key;
    private string? _publicKeySpkiBase64;
    private string? _keyId;

    public CommandSigner(
        IOptions<CommandSigningOptions> signingOptions,
        IOptions<NodesOptions> nodesOptions,
        ILogger<CommandSigner> logger)
    {
        _signingOptions = signingOptions.Value;
        _nodesOptions = nodesOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string PublicKeySpkiBase64 =>
        _publicKeySpkiBase64 ?? throw new InvalidOperationException("Command signer is not initialized.");

    /// <inheritdoc />
    public string KeyId =>
        _keyId ?? throw new InvalidOperationException("Command signer is not initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_key is not null)
            {
                return;
            }

            var path = ResolveKeyPath();
            var key = File.Exists(path) ? LoadKey(path) : CreateKey(path);
            var spki = key.ExportSubjectPublicKeyInfo();

            _key = key;
            _publicKeySpkiBase64 = Convert.ToBase64String(spki);
            _keyId = AgentCommandSigning.ComputeKeyId(spki);
            _logger.LogInformation("Command signing key ready (KeyId {KeyId})", _keyId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public AgentCommandEnvelope Sign(AgentCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.CommandId == Guid.Empty || envelope.NodeId == Guid.Empty || envelope.OrganizationId == Guid.Empty)
        {
            throw new ArgumentException("CommandId, NodeId and OrganizationId must be set.", nameof(envelope));
        }

        // C-2: the agent rejects a command whose lifetime is not positive or exceeds the limit, so never sign one.
        var lifetime = envelope.ExpiresAt - envelope.IssuedAt;
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromSeconds(AgentMessageLimits.MaxCommandLifetimeSeconds))
        {
            throw new ArgumentException(
                $"Command lifetime must be greater than zero and at most {AgentMessageLimits.MaxCommandLifetimeSeconds} seconds.",
                nameof(envelope));
        }

        var key = _key ?? throw new InvalidOperationException("Command signer is not initialized.");

        var input = AgentCommandSigning.BuildSigningInput(envelope);
        var signature = key.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return envelope with
        {
            Signature = Convert.ToBase64String(signature),
            SigningKeyId = _keyId!,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key?.Dispose();
        _key = null;
        _lock.Dispose();
    }

    private string ResolveKeyPath()
    {
        if (_signingOptions.KeyPath is not null)
        {
            if (!CommandSigningOptions.IsAcceptableKeyPath(_signingOptions.KeyPath))
            {
                throw new InvalidOperationException("CommandSigning:KeyPath must be an absolute path without '..' segments.");
            }

            return Path.GetFullPath(_signingOptions.KeyPath);
        }

        var directory = LocalFileCaStorageProvider.ResolveCaDirectory(_nodesOptions);
        return Path.GetFullPath(Path.Combine(directory, DefaultKeyFileName));
    }

    private static ECDsa LoadKey(string path)
    {
        var pemBytes = File.ReadAllBytes(path);
        char[]? pemChars = null;
#pragma warning disable CA2000 // Disposed in the catch; on success ownership passes to the caller.
        var key = ECDsa.Create();
#pragma warning restore CA2000
        try
        {
            pemChars = Encoding.UTF8.GetChars(pemBytes);
            key.ImportFromPem(pemChars);

            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid?.Value != P256OidValue)
            {
                throw new InvalidOperationException("Command signing key must be a P-256 key.");
            }

            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pemBytes);
            if (pemChars is not null)
            {
                Array.Clear(pemChars);
            }
        }
    }

    private ECDsa CreateKey(string path)
    {
#pragma warning disable CA2000 // Ownership of the key passes to the caller, which holds and disposes it.
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
#pragma warning restore CA2000
        byte[]? der = null;
        char[]? pem = null;
        byte[]? pemBytes = null;
        // Unique temp name: a leftover or concurrent temp file never collides with ours.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            der = key.ExportPkcs8PrivateKey();
            pem = PemEncoding.Write("PRIVATE KEY", der);
            pemBytes = Encoding.ASCII.GetBytes(pem);

            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            // The temp file is born owner-only, so the private key is never world-readable. It is moved into
            // place without overwriting: a concurrent starter that wins the move is loaded instead.
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, streamOptions))
            {
                stream.Write(pemBytes);
                stream.Flush(flushToDisk: true);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(tempPath, path, overwrite: false);

            _logger.LogWarning("Command signing key created at {KeyPath}. Back it up with the CA.", path);
            return key;
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another instance created the key first: discard ours and load theirs.
            key.Dispose();
            return LoadKey(path);
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // Best effort: the temp file holds the private key, so it is removed whenever it is left behind.
            }

            if (der is not null)
            {
                CryptographicOperations.ZeroMemory(der);
            }

            if (pem is not null)
            {
                Array.Clear(pem);
            }

            if (pemBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pemBytes);
            }
        }
    }
}