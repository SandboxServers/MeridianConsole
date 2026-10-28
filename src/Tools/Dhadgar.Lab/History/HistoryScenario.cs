using Dhadgar.Lab.Uat.Spec;

namespace Dhadgar.Lab.History;

/// <summary>
/// How a history client retries after its first enroll (C19 <c>retry</c>).
/// </summary>
public enum HistoryRetry
{
    /// <summary>No retry.</summary>
    None,

    /// <summary>The retry reuses the first attempt's key.</summary>
    SameKey,

    /// <summary>The retry uses a new key.</summary>
    NewKey,
}

/// <summary>
/// The barrier action applied to a client's first enroll (C19 <c>first_op_action</c>).
/// </summary>
public enum HistoryFirstOpAction
{
    /// <summary>The proxy holds the request until every client has arrived.</summary>
    HoldRequest,

    /// <summary>The proxy forwards the request and drops the response.</summary>
    DropResponse,
}

/// <summary>
/// One enrollment history scenario from <c>enroll-history.toml</c> (C19). <see cref="Clients"/> is the default; the
/// <c>history_run</c> call may override it for the race scenarios.
/// </summary>
/// <param name="Id">The scenario id, e.g. <c>enroll-race</c>.</param>
/// <param name="Clients">The default number of clients (1..8).</param>
/// <param name="Retry">The retry behaviour after the first enroll.</param>
/// <param name="FirstOpAction">The barrier action on the first enroll.</param>
/// <param name="Faults">The faults injected before the steps (C7 fault shape).</param>
public sealed record HistoryScenario(
    string Id,
    int Clients,
    HistoryRetry Retry,
    HistoryFirstOpAction FirstOpAction,
    IReadOnlyList<UatFault> Faults);
