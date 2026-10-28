using Dhadgar.Lab.Faults;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// One fault from the history: its target, when it started (null when only the heal is known) and when it healed (null while active).
/// </summary>
public sealed record FaultHistoryEntry(string FaultId, string Kind, string TargetId, DateTimeOffset? StartedAt, DateTimeOffset? HealedAt);

/// <summary>
/// The faults known to the lab, active and healed.
/// </summary>
public interface IFaultHistory
{
    /// <summary>The current snapshot of active and recently healed faults.</summary>
    IReadOnlyList<FaultHistoryEntry> Snapshot();
}

/// <summary>
/// Reads the fault history from the live <see cref="FaultBook"/>: active faults with their start, then the heal history.
/// </summary>
public sealed class FaultBookHistory(FaultBook book) : IFaultHistory
{
    /// <inheritdoc />
    public IReadOnlyList<FaultHistoryEntry> Snapshot()
    {
        var entries = new List<FaultHistoryEntry>();
        foreach (ActiveFault active in book.ListActive())
        {
            entries.Add(new FaultHistoryEntry(active.FaultId, active.Kind, active.TargetId, active.StartedAt, null));
        }

        foreach (FaultHealRecord healed in book.History())
        {
            entries.Add(new FaultHistoryEntry(healed.FaultId, healed.Kind, healed.TargetId, null, healed.HealedAt));
        }

        return entries;
    }
}

/// <summary>
/// The fault impact map (C10, D-ML46). A fault on target T can explain a 502, 503 or 504 answered by service S only when T is in
/// S's impact set. A fault on any other target never explains it.
/// </summary>
public static class FaultImpactMap
{
    /// <summary>
    /// How long after a heal a 502-504 is still explained.
    /// </summary>
    public static readonly TimeSpan HealGrace = TimeSpan.FromSeconds(10);

    private const string GatewayService = "gateway";

    /// <summary>
    /// The targets that can explain a failure answered by <paramref name="service"/>. For the Gateway the set is taken from the
    /// cluster the request path routes to; an unrouted path has an empty set.
    /// </summary>
    public static IReadOnlySet<string> ImpactSet(string service, string? requestPath)
    {
        if (string.Equals(service, GatewayService, StringComparison.OrdinalIgnoreCase))
        {
            string? cluster = GatewayRouteTable.ClusterFor(requestPath);
            if (cluster is null)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            HashSet<string> routed = ServiceCells(cluster);
            routed.Add("c:" + cluster);
            if (ProxyCatalog.Find("gateway-" + cluster) is not null)
            {
                routed.Add("gateway-" + cluster);
            }

            return routed;
        }

        return ServiceCells(service);
    }

    /// <summary>
    /// True when <paramref name="fault"/> explains a 502-504 answered by <paramref name="service"/> at <paramref name="at"/>: its target
    /// is in the impact set, and it was active at <paramref name="at"/> or healed less than 10 s before it.
    /// </summary>
    public static bool Explains(string service, string? requestPath, FaultHistoryEntry fault, DateTimeOffset at)
    {
        if (!ImpactSet(service, requestPath).Contains(fault.TargetId))
        {
            return false;
        }

        if (fault.StartedAt is DateTimeOffset started && started > at)
        {
            return false;
        }

        if (fault.HealedAt is null)
        {
            return true;
        }

        return at <= fault.HealedAt.Value + HealGrace;
    }

    private static HashSet<string> ServiceCells(string service)
    {
        var cells = new HashSet<string>(StringComparer.Ordinal) { "d:" + service };
        foreach (ProxyDefinition proxy in ProxyCatalog.All)
        {
            if (proxy.Name.StartsWith(service + "-", StringComparison.Ordinal))
            {
                cells.Add(proxy.Name);
            }
        }

        return cells;
    }
}
