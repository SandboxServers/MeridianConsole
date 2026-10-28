using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Docker;

/// <summary>
/// The kinds of fault target (C10). The id prefix follows the kind: <c>c:</c>, <c>p:</c>, <c>d:</c>, <c>f:</c>, <c>m:</c>.
/// </summary>
public enum LabTargetKind
{
    /// <summary>A container the lab created (<c>c:</c>).</summary>
    Container,

    /// <summary>A Toxiproxy proxy (<c>p:</c>).</summary>
    Proxy,

    /// <summary>A lab database (<c>d:</c>).</summary>
    Database,

    /// <summary>A logical file (<c>f:</c>).</summary>
    File,

    /// <summary>A pool member (<c>m:</c>).</summary>
    Member,
}

/// <summary>
/// One registered target. Only registered targets can be faulted or removed (R-BLAST).
/// </summary>
public sealed record LabTarget(string TargetId, LabTargetKind Kind, string Name, string? ContainerId, DateTimeOffset CreatedAt);

/// <summary>
/// The registry of lab-owned targets, persisted at <c>LabPaths.State\targets.json</c>.
/// </summary>
public interface ILabTargetRegistry
{
    /// <summary>
    /// Adds or replaces a target. Fails with <c>[Lab.InvalidArgument]</c> when the id prefix does not match the kind.
    /// </summary>
    Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId);

    /// <summary>
    /// The target with this id, or null.
    /// </summary>
    LabTarget? Find(string targetId);

    /// <summary>
    /// Every registered target.
    /// </summary>
    IReadOnlyList<LabTarget> All { get; }

    /// <summary>
    /// Removes a target and persists the change. Unknown ids are a no-op.
    /// </summary>
    void Remove(string targetId);

    /// <summary>
    /// Drops container entries whose container no longer exists. Returns how many were dropped.
    /// </summary>
    Task<int> ReconcileAsync(CancellationToken ct);
}

/// <summary>
/// File-backed <see cref="ILabTargetRegistry"/>. Writes go to a temp file and then replace the registry file.
/// A registry file that cannot be read starts the registry empty, which refuses every fault (the safe direction).
/// </summary>
public sealed class LabTargetRegistry : ILabTargetRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LabTarget> _targets = new(StringComparer.Ordinal);
    private readonly string _file;
    private readonly IDockerCli _docker;
    private readonly TimeProvider _time;

    /// <summary>
    /// Loads the registry from <see cref="LabPaths.State"/>.
    /// </summary>
    public LabTargetRegistry(LabPaths paths, IDockerCli docker, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _file = Path.Combine(paths.State, "targets.json");
        Load();
    }

    /// <inheritdoc />
    public IReadOnlyList<LabTarget> All
    {
        get
        {
            lock (_gate)
            {
                return _targets.Values.OrderBy(target => target.TargetId, StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <inheritdoc />
    public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(name);

        if (!HasPrefix(targetId, kind) || targetId.Length == PrefixLength(kind))
        {
            return Result<LabTarget>.Failure("[Lab.InvalidArgument] target id does not match its kind");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<LabTarget>.Failure("[Lab.InvalidArgument] target name is empty");
        }

        lock (_gate)
        {
            var entry = new LabTarget(targetId, kind, name, containerId, _time.GetUtcNow());
            _targets[targetId] = entry;
            return Persist().IsSuccess
                ? Result<LabTarget>.Success(entry)
                : Result<LabTarget>.Failure("[Lab.Io] target registry could not be written");
        }
    }

    /// <inheritdoc />
    public LabTarget? Find(string targetId)
    {
        ArgumentNullException.ThrowIfNull(targetId);

        lock (_gate)
        {
            return _targets.GetValueOrDefault(targetId);
        }
    }

    /// <inheritdoc />
    public void Remove(string targetId)
    {
        ArgumentNullException.ThrowIfNull(targetId);

        lock (_gate)
        {
            if (_targets.Remove(targetId))
            {
                _ = Persist();
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        List<LabTarget> containers;
        lock (_gate)
        {
            containers = _targets.Values.Where(target => target.Kind == LabTargetKind.Container && target.ContainerId is not null).ToList();
        }

        var dropped = 0;
        foreach (var target in containers)
        {
            var inspect = await _docker.RunAsync(DockerEngine.Linux, ["inspect", "--format", "{{.Id}}", target.ContainerId!], ct).ConfigureAwait(false);
            if (inspect.IsFailure)
            {
                // Docker itself is unavailable: keep every entry rather than drop the registry on a transient failure.
                return dropped;
            }

            if (inspect.Value.ExitCode == 0)
            {
                continue;
            }

            lock (_gate)
            {
                // Only drop the entry if it still names the container that was checked.
                if (_targets.TryGetValue(target.TargetId, out var current) && current.ContainerId == target.ContainerId)
                {
                    _targets.Remove(target.TargetId);
                    dropped++;
                }
            }
        }

        if (dropped > 0)
        {
            lock (_gate)
            {
                _ = Persist();
            }
        }

        return dropped;
    }

    private static bool HasPrefix(string targetId, LabTargetKind kind) =>
        targetId.StartsWith(Prefix(kind), StringComparison.Ordinal);

    private static int PrefixLength(LabTargetKind kind) => Prefix(kind).Length;

    private static string Prefix(LabTargetKind kind) => kind switch
    {
        LabTargetKind.Container => "c:",
        LabTargetKind.Proxy => "p:",
        LabTargetKind.Database => "d:",
        LabTargetKind.File => "f:",
        LabTargetKind.Member => "m:",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void Load()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<List<LabTarget>>(File.ReadAllText(_file), JsonOptions);
            foreach (var target in loaded ?? [])
            {
                _targets[target.TargetId] = target;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _targets.Clear();
        }
    }

    /// <summary>
    /// Writes the registry: a temp file in the same folder, then a move over the registry file. Call under <see cref="_gate"/>.
    /// </summary>
    private Result Persist()
    {
        var directory = Path.GetDirectoryName(_file)!;
        var temp = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(_targets.Values.ToList(), JsonOptions));
            File.Move(temp, _file, overwrite: true);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Best effort: the temp file is in the lab's own state folder.
            }

            return Result.Failure("[Lab.Io] target registry could not be written");
        }
    }
}
