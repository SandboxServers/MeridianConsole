using System.Text.Json;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The restarts the lab itself caused (wave 5 live gate). The dispatcher records the excuses for each successful call to
/// <c>svc_start</c>, <c>svc_restart</c>, <c>svc_stop</c>, <c>stack_up</c>, <c>stack_down</c> or <c>member_reset</c>, with the
/// start and end of the call (D-ML114). The target is the registry-name form (<c>meridian-lab-nodes</c>), which the I6 matcher
/// compares in canonical form. A restart with no recorded call is still unexcused.
/// </summary>
public sealed class LabRestartExcuses
{
    /// <summary>The target of <c>stack_up wipe=true</c> and <c>stack_down</c>: every lab container, bounded to the call (D-ML114).</summary>
    public const string AllTargets = "*";

    /// <summary>The infra containers that a plain <c>stack_up</c> creates (<c>LabStack.cs:236-277</c>).</summary>
    internal static readonly string[] StackUpContainers = ["postgres", "rabbitmq", "redis", "toxiproxy"];

    private const int MaxEntries = 4096;

    private readonly object _gate = new();
    private readonly List<RestartExcuse> _excuses = [];

    /// <summary>
    /// Records the excuses one successful call earns, from its start to its end. A tool that restarts nothing records nothing.
    /// </summary>
    public void RecordCall(string tool, JsonElement arguments, DateTimeOffset at, DateTimeOffset until)
    {
        IReadOnlyList<RestartExcuse> earned = ForCall(tool, arguments, at, until);
        if (earned.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            _excuses.AddRange(earned);
            if (_excuses.Count > MaxEntries)
            {
                _excuses.RemoveRange(0, _excuses.Count - MaxEntries);
            }
        }
    }

    /// <summary>
    /// The excuses recorded so far, oldest first. The I6 matcher filters them by time and target.
    /// </summary>
    public IReadOnlyList<RestartExcuse> Snapshot()
    {
        lock (_gate)
        {
            return _excuses.ToArray();
        }
    }

    /// <summary>
    /// The excuses one call earns. Only the target is read from the arguments; nothing is run or touched here.
    /// </summary>
    internal static IReadOnlyList<RestartExcuse> ForCall(string tool, JsonElement arguments, DateTimeOffset at, DateTimeOffset until)
    {
        switch (tool)
        {
            case "svc_start":
            case "svc_restart":
            case "svc_stop":
                string? service = ReadString(arguments, "service");
                return service is null
                    ? []
                    : [new RestartExcuse(tool, LabDefaults.ContainerPrefix + service, at, until)];

            case "stack_up":
                return ReadBool(arguments, "wipe")
                    ? [new RestartExcuse(tool, AllTargets, at, until)]
                    : StackUpContainers.Select(name => new RestartExcuse(tool, LabDefaults.ContainerPrefix + name, at, until)).ToArray();

            case "stack_down":
                return [new RestartExcuse(tool, AllTargets, at, until)];

            case "member_reset":
                string? member = ReadString(arguments, "member");
                return member is null
                    ? []
                    : [new RestartExcuse(tool, LabDefaults.ContainerPrefix + member, at, until)];

            default:
                return [];
        }
    }

    private static string? ReadString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool ReadBool(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;
}
