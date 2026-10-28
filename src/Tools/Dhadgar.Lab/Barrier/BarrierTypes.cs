namespace Dhadgar.Lab.Barrier;

/// <summary>
/// What the barrier proxy does with one armed operation (C19, D-ML47).
/// </summary>
public enum BarrierAction
{
    /// <summary>Forward the request and copy the response back.</summary>
    Pass,

    /// <summary>Hold the request before it reaches the upstream until its barrier is released (30 s at most).</summary>
    HoldRequest,

    /// <summary>Forward, read the whole response, then hold it for <see cref="BarrierRule.HoldMs"/> or until its barrier is released.</summary>
    HoldResponse,

    /// <summary>Forward, read the whole response, then abort the client connection so the client sees a reset after sending.</summary>
    DropResponse,
}

/// <summary>
/// One armed rule: the operation (its <c>runId/actor/seq</c> text), the action, the barrier name for the holds, and the hold time.
/// </summary>
/// <param name="OpId">The operation id text carried in <c>X-Lab-Op-Id</c>.</param>
/// <param name="Action">The action for requests of this operation.</param>
/// <param name="Barrier">The barrier a hold waits on; required for <see cref="BarrierAction.HoldRequest"/>.</param>
/// <param name="HoldMs">How long a <see cref="BarrierAction.HoldResponse"/> waits, in milliseconds.</param>
public sealed record BarrierRule(string OpId, BarrierAction Action, string? Barrier, int HoldMs);
