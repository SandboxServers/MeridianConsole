using System.Text.Json;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The restarts the lab itself caused (wave 5 live gate). The dispatcher records one excuse for each accepted call to
/// <c>svc_start</c>, <c>svc_restart</c>, <c>stack_up</c> or <c>member_reset</c>, with the target in the registry-name form the
/// I6 matcher compares (<c>meridian-lab-nodes</c>). A restart with no recorded call is still unexcused.
/// </summary>
public sealed class LabRestartExcuses
{
    /// <summary>The target of a stack-wide action: it excuses every container.</summary>
    public const string AllTargets = "*";

    private const int MaxEntries = 4096;

    private readonly object _gate = new();
    private readonly List<RestartExcuse> _excuses = [];

    /// <summary>
    /// Records the excuses one accepted call earns. A tool that restarts nothing records nothing.
    /// </summary>
    public void RecordCall(string tool, JsonElement arguments, DateTimeOffset at)
    {
        IReadOnlyList<RestartExcuse> earned = ForCall(tool, arguments, at);
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
    /// The excuses recorded so far, oldest first. The I6 matcher filters them by its own time window.
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
    internal static IReadOnlyList<RestartExcuse> ForCall(string tool, JsonElement arguments, DateTimeOffset at)
    {
        switch (tool)
        {
            case "svc_start":
            case "svc_restart":
                string? service = ReadString(arguments, "service");
                return service is null
                    ? []
                    : [new RestartExcuse(tool, LabDefaults.ContainerPrefix + service, at)];

            case "stack_up":
                return [new RestartExcuse(tool, AllTargets, at)];

            case "member_reset":
                string? member = ReadString(arguments, "member");
                return member is null
                    ? []
                    : [new RestartExcuse(tool, LabDefaults.ContainerPrefix + member, at)];

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
}
