using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// One registered container as Docker reports it: the restart count and the last start time.
/// </summary>
public sealed record RestartObservation(string Name, string Service, long RestartCount, DateTimeOffset? StartedAt);

/// <summary>
/// One audited action that may explain a restart (C11 I6 rule): <c>fault_kill</c>, <c>fault_pause</c>, <c>svc_start</c>,
/// <c>svc_restart</c>, <c>svc_stop</c>, <c>stack_up</c>, <c>stack_down</c> or <c>member_reset</c>, with its target (<c>c:&lt;name&gt;</c>,
/// a service or a member). <paramref name="Until"/> is the end of the call or the heal of a fault; null means <paramref name="At"/>.
/// </summary>
public sealed record RestartExcuse(string Action, string Target, DateTimeOffset At, DateTimeOffset? Until = null);

/// <summary>
/// I6 (C11): a registered container restarted, or the supervisor saw an unexpected process exit, with no excuse inside the
/// 30 s margins. An excuse covers an event only between its start and its end (D-ML114), and a restart count rise is excused
/// only by a fault. A baseline snapshot of <c>RestartCount</c> and <c>StartedAt</c> is taken by the first check; later checks
/// report every change since the previous check.
/// </summary>
public sealed class I6RestartInvariant : IInvariant
{
    /// <summary>The margin on each side of the window within which an excuse counts.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<RestartObservation>>>> _observe;
    private readonly Func<IReadOnlyList<RestartExcuse>> _excuses;
    private readonly Func<IReadOnlyList<ProcessExit>> _exits;
    private readonly Dictionary<string, RestartObservation> _baseline = new(StringComparer.Ordinal);
    private bool _hasBaseline;

    /// <summary>
    /// Creates the check over the container inspector, the audited actions and the supervisor's exit events.
    /// </summary>
    public I6RestartInvariant(
        Func<CancellationToken, Task<Result<IReadOnlyList<RestartObservation>>>> observe,
        Func<IReadOnlyList<RestartExcuse>> excuses,
        Func<IReadOnlyList<ProcessExit>> exits)
    {
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        _excuses = excuses ?? throw new ArgumentNullException(nameof(excuses));
        _exits = exits ?? throw new ArgumentNullException(nameof(exits));
    }

    /// <inheritdoc />
    public string Id => "I6";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        Result<IReadOnlyList<RestartObservation>> observed = await _observe(ct).ConfigureAwait(false);
        if (observed.IsFailure)
        {
            return Result<IReadOnlyList<InvariantViolation>>.Failure(observed.Error);
        }

        var violations = new List<InvariantViolation>();
        IReadOnlyList<RestartExcuse> excuses = _excuses();

        if (_hasBaseline)
        {
            foreach (RestartObservation now in observed.Value!)
            {
                if (!_baseline.TryGetValue(now.Name, out RestartObservation? before))
                {
                    continue;
                }

                bool countRose = now.RestartCount > before.RestartCount;
                bool restarted = countRose
                    || (now.StartedAt is DateTimeOffset started && before.StartedAt is DateTimeOffset previous && started > previous);
                DateTimeOffset at = now.StartedAt ?? window.To;
                if (restarted && !Excused(excuses, now.Name, at, countRose))
                {
                    violations.Add(new InvariantViolation(
                        Id,
                        now.Service,
                        "restart",
                        $"container {now.Name} restarted (RestartCount {before.RestartCount} to {now.RestartCount}) with no excusing action",
                        window.To,
                        null));
                }
            }
        }

        foreach (ProcessExit exit in _exits())
        {
            if (exit.Expected || exit.At < window.From || exit.At > window.To)
            {
                continue;
            }

            if (!Excused(excuses, exit.Name, exit.At, countRose: false))
            {
                violations.Add(new InvariantViolation(
                    Id,
                    exit.Name,
                    "process_exit",
                    $"process {exit.Name} exited unexpectedly (code {exit.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}) with no excusing action",
                    exit.At,
                    null));
            }
        }

        _baseline.Clear();
        foreach (RestartObservation item in observed.Value!)
        {
            _baseline[item.Name] = item;
        }

        _hasBaseline = true;
        return Result<IReadOnlyList<InvariantViolation>>.Success(violations);
    }

    /// <summary>
    /// True when an audited action excuses this event under the C11 I6 rule and D-ML114: the event time falls inside the
    /// action's window (with the margins), and the target names this container. A count rise is excused only by a fault.
    /// </summary>
    internal static bool Excused(IReadOnlyList<RestartExcuse> excuses, string name, DateTimeOffset at, bool countRose)
    {
        string subject = Canonical(name);
        foreach (RestartExcuse excuse in excuses)
        {
            if (!Covers(excuse, at))
            {
                continue;
            }

            bool sameTarget = Canonical(excuse.Target) == subject || excuse.Target == name;
            bool matches = excuse.Action switch
            {
                "fault_kill" or "fault_pause" => sameTarget,
                "svc_start" or "svc_restart" or "svc_stop" or "member_reset" => !countRose && sameTarget,
                "stack_up" or "stack_down" => !countRose && (sameTarget || excuse.Target == LabRestartExcuses.AllTargets),
                _ => false,
            };
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the event time is within the margin of the excuse's window, from its start to its end (or its start alone).
    /// </summary>
    internal static bool Covers(RestartExcuse excuse, DateTimeOffset at) =>
        at + Margin >= excuse.At && at - Margin <= (excuse.Until ?? excuse.At);

    /// <summary>
    /// The container or process name without a leading <c>c:</c> and without the lab container prefix.
    /// </summary>
    internal static string Canonical(string target)
    {
        string name = target.StartsWith("c:", StringComparison.Ordinal) ? target[2..] : target;
        return name.StartsWith(LabDefaults.ContainerPrefix, StringComparison.Ordinal) ? name[LabDefaults.ContainerPrefix.Length..] : name;
    }
}
