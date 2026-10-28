using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Stores the agent identity as <c>agent-state.json</c> in <see cref="AgentOptions.StateDirectory"/>.
/// The file holds no secret key material, but it holds the command trust anchor (the public signing key), so a
/// damaged file is a failure, never a silent re-enrollment or a key the agent cannot verify with.
/// </summary>
public sealed class FileAgentStateStore : IAgentStateStore
{
    /// <summary>
    /// Name of the state file inside the state directory.
    /// </summary>
    public const string FileName = "agent-state.json";

    /// <summary>
    /// Largest state file that is read (64 KB). A real file is a few hundred bytes.
    /// </summary>
    private const long MaxFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOptions<AgentOptions> _options;
    private readonly IPathValidator _pathValidator;
    private readonly IStateFileSecurity _fileSecurity;
    private readonly ILogger<FileAgentStateStore> _logger;

    public FileAgentStateStore(
        IOptions<AgentOptions> options,
        IPathValidator pathValidator,
        IStateFileSecurity fileSecurity,
        ILogger<FileAgentStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<StoredAgentState>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var pathResult = ResolveStatePath();
        if (pathResult.IsFailure)
        {
            return Result<StoredAgentState>.Failure(pathResult.Error!);
        }

        var path = pathResult.Value!;

        try
        {
            // Open directly. File.Exists reports false on access denied, which would turn an unreadable file into "first run".
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, System.IO.FileOptions.Asynchronous);

            var trusted = _fileSecurity.VerifyTrusted(path);
            if (trusted.IsFailure)
            {
                _logger.LogCritical("Agent state file is not trusted: {Error}", trusted.Error);
                return Result<StoredAgentState>.Failure(trusted.Error!);
            }

            if (stream.Length > MaxFileBytes)
            {
                _logger.LogWarning("Agent state file is larger than the {Max} byte limit", MaxFileBytes);
                return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file exceeds the size limit");
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);

            var state = JsonSerializer.Deserialize<AgentStateFile>(bytes, JsonOptions);
            if (state is null || state.NodeId == Guid.Empty || state.OrganizationId == Guid.Empty)
            {
                _logger.LogWarning("Agent state file is missing a node or organization ID");
                return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file does not hold a valid identity");
            }

            // The signing key is stored as a pair; one field without the other is a damaged file.
            if ((state.CommandSigningPublicKey is null) != (state.CommandSigningKeyId is null))
            {
                _logger.LogWarning("Agent state file holds only one of the command signing key fields");
                return Result<StoredAgentState>.Failure(
                    "[AgentState.Corrupt] State file holds a partial command signing key");
            }

            // A key present in the file must pass the enrollment-time checks. Empty strings are present, so they fail.
            if (state.CommandSigningPublicKey is not null
                && !AgentSigningKeyValidation.IsValid(state.CommandSigningPublicKey, state.CommandSigningKeyId))
            {
                _logger.LogWarning("Agent state file holds an invalid command signing key");
                return Result<StoredAgentState>.Failure(
                    "[AgentState.Corrupt] State file holds an invalid command signing key");
            }

            return Result<StoredAgentState>.Success(
                new StoredAgentState(new AgentIdentity(
                    state.NodeId,
                    state.OrganizationId,
                    state.EnrolledAt,
                    state.CommandSigningPublicKey,
                    state.CommandSigningKeyId)));
        }
        catch (FileNotFoundException)
        {
            return Result<StoredAgentState>.Success(StoredAgentState.None);
        }
        catch (DirectoryNotFoundException)
        {
            return Result<StoredAgentState>.Success(StoredAgentState.None);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Agent state file is not valid JSON");
            return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file is not valid JSON");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read agent state file");
            return Result<StoredAgentState>.Failure("[AgentState.Io] Failed to read agent state file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied reading agent state file");
            return Result<StoredAgentState>.Failure("[AgentState.Io] Access denied reading agent state file");
        }
    }

    /// <inheritdoc />
    public async Task<Result> SaveAsync(AgentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.NodeId == Guid.Empty || identity.OrganizationId == Guid.Empty)
        {
            return Result.Failure("[AgentState.Invalid] Identity must have a node and organization ID");
        }

        var pathResult = ResolveStatePath();
        if (pathResult.IsFailure)
        {
            return Result.Failure(pathResult.Error!);
        }

        var finalPath = pathResult.Value!;
        var tempPath = finalPath + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(finalPath)!;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                // 0700 when the agent creates it; an existing directory keeps its mode
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            // A stale or planted temp file is removed, never adopted (CreateNew below fails if one reappears).
            TryDeleteTemp(tempPath);

            var json = JsonSerializer.SerializeToUtf8Bytes(
                new AgentStateFile(
                    identity.NodeId,
                    identity.OrganizationId,
                    identity.EnrolledAt,
                    identity.CommandSigningPublicKey,
                    identity.CommandSigningKeyId), JsonOptions);

            var created = _fileSecurity.CreateNew(tempPath);
            if (created.IsFailure)
            {
                _logger.LogError("Failed to create agent state temp file: {Error}", created.Error);
                return Result.Failure(created.Error!);
            }

            await using (var stream = created.Value!)
            {
                await stream.WriteAsync(json, cancellationToken);
#pragma warning disable CA1849 // Flush(bool) has no async overload; FlushAsync does not reach the disk
                stream.Flush(flushToDisk: true); // durable before the rename
#pragma warning restore CA1849
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return Result.Success();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to write agent state file");
            return Result.Failure("[AgentState.Io] Failed to write agent state file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied writing agent state file");
            return Result.Failure("[AgentState.Io] Access denied writing agent state file");
        }
        finally
        {
            // Best-effort: never leave a partial temp file behind
            TryDeleteTemp(tempPath);
        }
    }

    /// <summary>
    /// Resolves the state file path and validates it against the state directory.
    /// </summary>
    private Result<string> ResolveStatePath()
    {
        var directory = _options.Value.StateDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Result<string>.Failure("[AgentState.Config] StateDirectory is not configured");
        }

        if (AgentOptions.CheckStateDirectory(directory) is { } error)
        {
            return Result<string>.Failure("[AgentState.Config] " + error);
        }

        var candidate = Path.Combine(directory, FileName);
        return _pathValidator.ValidatePath(candidate, [directory]);
    }

    private static void TryDeleteTemp(string tempPath)
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
            // Ignored: the temp file is removed on the next successful save
        }
        catch (UnauthorizedAccessException)
        {
            // Ignored: the temp file is removed on the next successful save
        }
    }

    /// <summary>
    /// On-disk shape of the state file. Field names follow the web defaults (camelCase).
    /// </summary>
    private sealed record AgentStateFile(
        Guid NodeId,
        Guid OrganizationId,
        DateTimeOffset EnrolledAt,
        string? CommandSigningPublicKey = null,
        string? CommandSigningKeyId = null);
}
