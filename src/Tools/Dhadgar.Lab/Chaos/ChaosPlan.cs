using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Random;
using Dhadgar.Shared.Results;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Chaos;

/// <summary>
/// One fault a chaos plan may inject: a C4 fault tool with its arguments, a selection weight, and the seeded duration range.
/// </summary>
/// <param name="Tool">The C4 fault tool name.</param>
/// <param name="Args">The tool arguments, as written in the plan.</param>
/// <param name="Weight">The selection weight, 1 to 100.</param>
/// <param name="MinDurationS">The shortest duration in seconds.</param>
/// <param name="MaxDurationS">The longest duration in seconds.</param>
public sealed record ChaosFault(string Tool, JsonObject Args, int Weight, int MinDurationS, int MaxDurationS);

/// <summary>The workload a chaos run loops: a UAT spec and its rows.</summary>
/// <param name="Spec">The spec path, relative to the repository root.</param>
/// <param name="Rows">The row ids to run on each loop.</param>
public sealed record ChaosWorkload(string Spec, IReadOnlyList<string> Rows);

/// <summary>
/// A validated chaos plan (<c>docs/guides/uat-specs/chaos/&lt;name&gt;.toml</c>, L29). <see cref="Schedule"/> builds the seeded
/// timeline of injections.
/// </summary>
/// <param name="Id">The plan id.</param>
/// <param name="Workload">The looped workload.</param>
/// <param name="MaxConcurrent">The most faults active at once.</param>
/// <param name="MinQuietS">The shortest gap between injection starts, in seconds.</param>
/// <param name="MaxQuietS">The longest gap between injection starts, in seconds.</param>
/// <param name="Faults">The faults the scheduler picks from.</param>
public sealed record ChaosPlan(
    string Id,
    ChaosWorkload Workload,
    int MaxConcurrent,
    int MinQuietS,
    int MaxQuietS,
    IReadOnlyList<ChaosFault> Faults)
{
    /// <summary>
    /// Builds the injection timeline for <paramref name="durationS"/> seconds. The stream is <c>LabRandom(seed, "chaos")</c>
    /// (R-SEED): the same plan, seed and duration always give the same entries. Each entry draws its gap, then its fault by
    /// weight, then its duration, in that order.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="durationS">The run length in seconds; no entry starts at or after it.</param>
    public IReadOnlyList<ChaosScheduleEntry> Schedule(ulong seed, int durationS)
    {
        var rng = new LabRandom(seed, "chaos");
        int totalWeight = Faults.Sum(fault => fault.Weight);
        var entries = new List<ChaosScheduleEntry>();
        int at = 0;
        while (true)
        {
            at += rng.Next(MinQuietS, MaxQuietS + 1);
            if (at >= durationS)
            {
                return entries;
            }

            int pick = rng.Next(0, totalWeight);
            int index = 0;
            int running = 0;
            for (int i = 0; i < Faults.Count; i++)
            {
                running += Faults[i].Weight;
                if (pick < running)
                {
                    index = i;
                    break;
                }
            }

            int duration = rng.Next(Faults[index].MinDurationS, Faults[index].MaxDurationS + 1);
            entries.Add(new ChaosScheduleEntry(at, index, duration));
        }
    }
}

/// <summary>One scheduled injection: the second it starts, the index into <see cref="ChaosPlan.Faults"/>, and how long it lasts.</summary>
/// <param name="StartS">Seconds from the start of the run.</param>
/// <param name="FaultIndex">The index of the fault in the plan.</param>
/// <param name="DurationS">Seconds until the fault is healed.</param>
public sealed record ChaosScheduleEntry(int StartS, int FaultIndex, int DurationS);

/// <summary>
/// Loads and validates chaos plans (R-BLAST). A plan may name only the C4 fault injection tools, and a proxy must be a C10 proxy;
/// anything else is refused before a job starts.
/// </summary>
public static class ChaosPlanLoader
{
    /// <summary>The plan id pattern, the same as the spec and twin ids.</summary>
    public static readonly Regex IdPattern = new("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The C4 fault injection tools a plan may name. Heal, reset and the read tools are not injections.</summary>
    public static readonly IReadOnlySet<string> FaultTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "fault_toxic_add",
        "fault_proxy_disable",
        "fault_partition",
        "fault_kill",
        "fault_pause",
        "fault_disk_fill",
        "fault_corrupt_file",
        "fault_corrupt_registry",
        "fault_db",
    };

    /// <summary>The longest duration or gap a plan may name, in seconds.</summary>
    public const int MaxSeconds = 3600;

    private const int MaxFaults = 20;

    /// <summary>
    /// Reads a plan file. The path must lie under <paramref name="root"/> (R-PATH); a path outside it is refused.
    /// </summary>
    /// <param name="path">The full path of the plan file.</param>
    /// <param name="root">The chaos plan folder.</param>
    /// <param name="validator">The path validator.</param>
    public static Result<ChaosPlan> LoadFile(string path, string root, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        Result<string> checkedPath = validator.ValidatePath(path, [root]);
        if (checkedPath.IsFailure)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] the chaos plan is outside the chaos plan folder");
        }

        string text;
        try
        {
            if (!File.Exists(checkedPath.Value!))
            {
                return Result<ChaosPlan>.Failure("[Lab.NotFound] the chaos plan does not exist");
            }

            text = File.ReadAllText(checkedPath.Value!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ChaosPlan>.Failure("[Lab.Unavailable] the chaos plan could not be read");
        }

        return Load(text);
    }

    /// <summary>
    /// Parses and validates a plan from TOML text. Returns a failure with a fixed message for every rule it breaks.
    /// </summary>
    /// <param name="text">The plan text.</param>
    public static Result<ChaosPlan> Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or TomlException)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] the chaos plan is not valid TOML");
        }

        if (!root.TryGetValue("schema", out object? schema) || schema is not long schemaValue || schemaValue != 1)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] schema must be 1");
        }

        if (!root.TryGetValue("plan", out object? planRaw) || planRaw is not TomlTable plan)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] plan must be a table");
        }

        if (!plan.TryGetValue("id", out object? idRaw) || idRaw is not string id || !IdPattern.IsMatch(id))
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] id must match ^[a-z0-9-]{1,64}$");
        }

        if (!plan.TryGetValue("workload", out object? workloadRaw) || workloadRaw is not TomlTable workload)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] workload must be a table with spec and rows");
        }

        if (!workload.TryGetValue("spec", out object? specRaw) || specRaw is not string spec || spec.Length == 0
            || !TryStrings(workload, "rows", out IReadOnlyList<string>? rows))
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] workload needs a spec path and a non-empty rows list");
        }

        if (!TryInt(plan, "max_concurrent", 1, 4, out int maxConcurrent))
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] max_concurrent must be 1 to 4");
        }

        if (!TryRange(plan, "quiet_s", 1, MaxSeconds, out int minQuiet, out int maxQuiet))
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] quiet_s must be [min, max] with 1 <= min <= max <= 3600");
        }

        if (!root.TryGetValue("fault", out object? faultRaw) || faultRaw is not TomlTableArray faultArray
            || faultArray.Count == 0 || faultArray.Count > MaxFaults)
        {
            return Result<ChaosPlan>.Failure("[Lab.InvalidArgument] the plan needs one to 20 [[fault]] tables");
        }

        var faults = new List<ChaosFault>();
        foreach (object item in faultArray)
        {
            Result<ChaosFault> fault = ReadFault((TomlTable)item);
            if (fault.IsFailure)
            {
                return Result<ChaosPlan>.Failure(fault.Error);
            }

            faults.Add(fault.Value!);
        }

        return Result<ChaosPlan>.Success(new ChaosPlan(id, new ChaosWorkload(spec, rows!), maxConcurrent, minQuiet, maxQuiet, faults));
    }

    private static Result<ChaosFault> ReadFault(TomlTable table)
    {
        if (!table.TryGetValue("tool", out object? toolRaw) || toolRaw is not string tool || !FaultTools.Contains(tool))
        {
            return Result<ChaosFault>.Failure("[Lab.InvalidArgument] tool must be a C4 fault injection tool");
        }

        if (!table.TryGetValue("args", out object? argsRaw) || argsRaw is not TomlTable argsTable)
        {
            return Result<ChaosFault>.Failure("[Lab.InvalidArgument] args must be a table");
        }

        if (RequiresProxy(tool)
            && (!argsTable.TryGetValue("proxy", out object? proxyRaw) || proxyRaw is not string proxy || ProxyCatalog.Find(proxy) is null))
        {
            return Result<ChaosFault>.Failure("[Lab.InvalidArgument] proxy must be a C10 proxy name");
        }

        int weight = 1;
        if (table.ContainsKey("weight") && !TryInt(table, "weight", 1, 100, out weight))
        {
            return Result<ChaosFault>.Failure("[Lab.InvalidArgument] weight must be 1 to 100");
        }

        if (!TryRange(table, "duration_s", 1, MaxSeconds, out int minDuration, out int maxDuration))
        {
            return Result<ChaosFault>.Failure("[Lab.InvalidArgument] duration_s must be [min, max] with 1 <= min <= max <= 3600");
        }

        return Result<ChaosFault>.Success(new ChaosFault(tool, ToObject(argsTable), weight, minDuration, maxDuration));
    }

    private static bool RequiresProxy(string tool) =>
        tool is "fault_toxic_add" or "fault_proxy_disable";

    private static bool TryInt(TomlTable table, string key, int min, int max, out int value)
    {
        if (table.TryGetValue(key, out object? raw) && raw is long number && number >= min && number <= max)
        {
            value = (int)number;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryRange(TomlTable table, string key, int min, int max, out int low, out int high)
    {
        low = 0;
        high = 0;
        if (!table.TryGetValue(key, out object? raw) || raw is not TomlArray array || array.Count != 2
            || array[0] is not long lo || array[1] is not long hi || lo < min || hi > max || lo > hi)
        {
            return false;
        }

        low = (int)lo;
        high = (int)hi;
        return true;
    }

    private static bool TryStrings(TomlTable table, string key, out IReadOnlyList<string>? values)
    {
        values = null;
        if (!table.TryGetValue(key, out object? raw) || raw is not TomlArray array || array.Count == 0)
        {
            return false;
        }

        var list = new List<string>();
        foreach (object? item in array)
        {
            if (item is not string text || text.Length == 0)
            {
                return false;
            }

            list.Add(text);
        }

        values = list;
        return true;
    }

    private static JsonObject ToObject(TomlTable table)
    {
        var obj = new JsonObject();
        foreach (KeyValuePair<string, object> pair in table)
        {
            obj[pair.Key] = ToNode(pair.Value);
        }

        return obj;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        long number => JsonValue.Create(number),
        double real => JsonValue.Create(real),
        bool flag => JsonValue.Create(flag),
        TomlTable table => ToObject(table),
        TomlArray array => new JsonArray(array.Select(item => ToNode(item)).ToArray()),
        _ => JsonValue.Create(value.ToString()),
    };
}
