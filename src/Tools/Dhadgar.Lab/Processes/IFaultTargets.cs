namespace Dhadgar.Lab.Processes;

/// <summary>
/// Tells the watchdog which targets a fault currently holds. A faulted target's process is never relaunched and its exit is
/// not counted as a crash. L19's <c>FaultBook</c> implements this.
/// </summary>
public interface IFaultTargets
{
    /// <summary>
    /// True when the target (<c>c:&lt;name&gt;</c> or <c>proc:&lt;name&gt;</c>) is under an active fault.
    /// </summary>
    bool IsFaulted(string targetId);
}

/// <summary>
/// The default: no target is faulted.
/// </summary>
public sealed class NoFaults : IFaultTargets
{
    /// <inheritdoc />
    public bool IsFaulted(string targetId) => false;
}
