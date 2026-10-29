using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Invariants;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Soak;

/// <summary>
/// One resource sample: every series' value at one instant. Series keys are <c>memory_mib:&lt;container&gt;</c>,
/// <c>pids:&lt;container&gt;</c> and <c>handles:daemon</c>.
/// </summary>
public sealed record ResourceSampleSet(DateTimeOffset At, IReadOnlyDictionary<string, double> Values);

/// <summary>
/// The resource sampler for soak runs (L29, A4). Every sample reads <c>docker stats --no-stream</c> for the registered
/// container targets that carry <c>meridian-lab=1</c>, passed by id through <c>ArgumentList</c>. It never uses <c>--all</c>
/// and never matches a name pattern. It also reads the daemon's own handle count when a reader is given.
/// </summary>
public sealed class ResourceSampler
{
    /// <summary>The series prefix for a container's memory in MiB.</summary>
    public const string MemorySeries = "memory_mib";

    /// <summary>The series prefix for a container's process count.</summary>
    public const string PidsSeries = "pids";

    /// <summary>The series prefix for the daemon's handle count.</summary>
    public const string HandlesSeries = "handles";

    /// <summary>The series key of the daemon's handle count.</summary>
    public const string DaemonHandlesKey = HandlesSeries + ":daemon";

    private const double MiB = 1024d * 1024d;

    private static readonly Dictionary<string, double> UnitBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = 1d,
        ["kB"] = 1000d,
        ["KiB"] = 1024d,
        ["MB"] = 1000d * 1000d,
        ["MiB"] = MiB,
        ["GB"] = 1000d * 1000d * 1000d,
        ["GiB"] = 1024d * MiB,
        ["TB"] = 1000d * 1000d * 1000d * 1000d,
        ["TiB"] = 1024d * 1024d * MiB,
    };

    private readonly IDockerCli _docker;
    private readonly ILabTargetRegistry _registry;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> _labelled;
    private readonly TimeProvider _time;
    private readonly Func<long>? _daemonHandles;

    /// <summary>
    /// Creates the sampler.
    /// </summary>
    /// <param name="docker">The docker boundary (R-DOCKER).</param>
    /// <param name="registry">The lab target registry; only its container targets are sampled.</param>
    /// <param name="labelled">The names of the containers that carry <c>meridian-lab=1</c> (<see cref="InvariantWiring.LabelledContainers"/>).</param>
    /// <param name="time">The clock.</param>
    /// <param name="daemonHandles">The daemon's own handle count; null leaves the daemon series out.</param>
    public ResourceSampler(
        IDockerCli docker,
        ILabTargetRegistry registry,
        Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> labelled,
        TimeProvider time,
        Func<long>? daemonHandles = null)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _labelled = labelled ?? throw new ArgumentNullException(nameof(labelled));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _daemonHandles = daemonHandles;
    }

    /// <summary>
    /// The handle count of this process (Windows); zero where the platform does not report it.
    /// </summary>
    public static long DaemonHandleCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        using Process process = Process.GetCurrentProcess();
        return process.HandleCount;
    }

    /// <summary>
    /// Takes one sample. A failure means the sample could not be read (docker exit code, unreadable output); the caller records
    /// it, and I7 reports it as a check error, never as a skip.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<ResourceSampleSet>> SampleAsync(CancellationToken ct)
    {
        DateTimeOffset at = _time.GetUtcNow();
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (_daemonHandles is not null)
        {
            values[DaemonHandlesKey] = _daemonHandles();
        }

        LabTarget[] containers = _registry.All.Where(t => t.Kind == LabTargetKind.Container).ToArray();
        if (containers.Length == 0)
        {
            return Result<ResourceSampleSet>.Success(new ResourceSampleSet(at, values));
        }

        Result<IReadOnlyList<string>> labelled = await _labelled(ct).ConfigureAwait(false);
        if (labelled.IsFailure)
        {
            return Result<ResourceSampleSet>.Failure(labelled.Error);
        }

        var labelledNames = labelled.Value!.ToHashSet(StringComparer.Ordinal);
        string[] ids = containers
            .Where(t => labelledNames.Contains(t.Name))
            .Select(t => t.ContainerId ?? t.Name)
            .ToArray();

        // Never call docker stats with no ids: with no argument it would report every running container.
        if (ids.Length == 0)
        {
            return Result<ResourceSampleSet>.Success(new ResourceSampleSet(at, values));
        }

        var args = new List<string> { "stats", "--no-stream", "--format", "{{json .}}" };
        args.AddRange(ids);
        Result<DockerOutput> run = await _docker.RunAsync(DockerEngine.Linux, args, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<ResourceSampleSet>.Failure(run.Error);
        }

        if (run.Value!.ExitCode != 0)
        {
            return Result<ResourceSampleSet>.Failure("[Lab.Docker] docker stats failed");
        }

        foreach (string raw in run.Value.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Result read = ReadLine(raw, labelledNames, values);
            if (read.IsFailure)
            {
                return Result<ResourceSampleSet>.Failure(read.Error);
            }
        }

        return Result<ResourceSampleSet>.Success(new ResourceSampleSet(at, values));
    }

    /// <summary>
    /// Reads one <c>docker stats --format "{{json .}}"</c> line for a labelled container. Lines for any other name are ignored.
    /// </summary>
    internal static Result ReadLine(string line, IReadOnlySet<string> labelledNames, IDictionary<string, double> values)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("MemUsage", out JsonElement memElement) || memElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("PIDs", out JsonElement pidsElement) || pidsElement.ValueKind != JsonValueKind.String)
            {
                return Result.Failure("[Lab.Parse] docker stats output could not be read");
            }

            string name = nameElement.GetString()!;
            if (!labelledNames.Contains(name))
            {
                return Result.Success();
            }

            Result<double> memory = MemoryMiB(memElement.GetString()!);
            if (memory.IsFailure)
            {
                return Result.Failure(memory.Error);
            }

            if (!long.TryParse(pidsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long pids))
            {
                return Result.Failure("[Lab.Parse] docker stats output could not be read");
            }

            values[MemorySeries + ":" + name] = memory.Value!;
            values[PidsSeries + ":" + name] = pids;
            return Result.Success();
        }
        catch (JsonException)
        {
            return Result.Failure("[Lab.Parse] docker stats output could not be read");
        }
    }

    /// <summary>
    /// The memory in use from docker's <c>MemUsage</c> (for example <c>23.45MiB / 7.67GiB</c>), in MiB.
    /// </summary>
    internal static Result<double> MemoryMiB(string usage)
    {
        string used = usage.Split('/')[0].Trim();
        int split = 0;
        while (split < used.Length && (char.IsDigit(used[split]) || used[split] == '.'))
        {
            split++;
        }

        if (split == 0
            || !double.TryParse(used[..split], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
            || !UnitBytes.TryGetValue(used[split..].Trim(), out double unit))
        {
            return Result<double>.Failure("[Lab.Parse] docker stats memory could not be read");
        }

        return Result<double>.Success(amount * unit / MiB);
    }
}

/// <summary>
/// The shared sample store of the soak run. <see cref="SoakJob"/> writes it every 30 seconds while a soak runs; I7 reads it.
/// Samples and sampler errors are kept only while a soak is active.
/// </summary>
public sealed class ResourceSampleStore
{
    private readonly object _gate = new();
    private readonly List<ResourceSampleSet> _samples = [];
    private bool _active;
    private string? _lastError;

    /// <summary>True while a soak run is active.</summary>
    public bool IsSoaking
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>The redacted error of the last sample, if it failed and no sample has succeeded since.</summary>
    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <summary>Starts a soak: clears the samples and any error.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            _active = true;
            _samples.Clear();
            _lastError = null;
        }
    }

    /// <summary>Ends the soak. The samples stay readable until the next <see cref="Begin"/>.</summary>
    public void End()
    {
        lock (_gate)
        {
            _active = false;
        }
    }

    /// <summary>Records one good sample while a soak is active.</summary>
    public void Add(ResourceSampleSet sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            if (_active)
            {
                _samples.Add(sample);
                _lastError = null;
            }
        }
    }

    /// <summary>Records a failed sample while a soak is active. The message must already be redacted.</summary>
    public void Fail(string error)
    {
        lock (_gate)
        {
            if (_active)
            {
                _lastError = error;
            }
        }
    }

    /// <summary>The samples of the current or last soak, oldest first.</summary>
    public IReadOnlyList<ResourceSampleSet> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }
}
