using System.Text.Json;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Stores the agent identity as <c>agent-state.json</c> in <see cref="AgentOptions.StateDirectory"/>.
/// The file holds no secrets. A corrupt file is a failure, never a silent re-enrollment.
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
    private readonly ILogger<FileAgentStateStore> _logger;

    public FileAgentStateStore(
        IOptions<AgentOptions> options,
        IPathValidator pathValidator,
        ILogger<FileAgentStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<AgentIdentity?>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var pathResult = ResolveStatePath();
        if (pathResult.IsFailure)
        {
            return Result<AgentIdentity?>.Failure(pathResult.Error!);
        }

        var path = pathResult.Value!;

        try
        {
            if (!File.Exists(path))
            {
                // The shared Result<T> cannot carry a successful null, so "no state yet" is a distinct failure code.
                return Result<AgentIdentity?>.Failure("[AgentState.Missing] No agent state file exists yet");
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, System.IO.FileOptions.Asynchronous);

            if (stream.Length > MaxFileBytes)
            {
                _logger.LogWarning("Agent state file is larger than the {Max} byte limit", MaxFileBytes);
                return Result<AgentIdentity?>.Failure("[AgentState.Corrupt] State file exceeds the size limit");
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);

            var state = JsonSerializer.Deserialize<AgentStateFile>(bytes, JsonOptions);
            if (state is null || state.NodeId == Guid.Empty || state.OrganizationId == Guid.Empty)
            {
                _logger.LogWarning("Agent state file is missing a node or organization ID");
                return Result<AgentIdentity?>.Failure("[AgentState.Corrupt] State file does not hold a valid identity");
            }

            return Result<AgentIdentity?>.Success(
                new AgentIdentity(state.NodeId, state.OrganizationId, state.EnrolledAt));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Agent state file is not valid JSON");
            return Result<AgentIdentity?>.Failure("[AgentState.Corrupt] State file is not valid JSON");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read agent state file");
            return Result<AgentIdentity?>.Failure("[AgentState.Io] Failed to read agent state file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied reading agent state file");
            return Result<AgentIdentity?>.Failure("[AgentState.Io] Access denied reading agent state file");
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
            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(
                new AgentStateFile(identity.NodeId, identity.OrganizationId, identity.EnrolledAt), JsonOptions);

            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, System.IO.FileOptions.Asynchronous))
            {
                await stream.WriteAsync(json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
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
            return Result<string>.Failure("[AgentState.Io] StateDirectory is not configured");
        }

        if (!Path.IsPathRooted(directory))
        {
            return Result<string>.Failure("[AgentState.Io] StateDirectory must be an absolute path");
        }

        var segments = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Contains(".."))
        {
            return Result<string>.Failure("[AgentState.Io] StateDirectory must not contain '..' segments");
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
    private sealed record AgentStateFile(Guid NodeId, Guid OrganizationId, DateTimeOffset EnrolledAt);
}
