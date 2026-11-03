using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Data;

/// <summary>Change-tracker helpers for retried units of work.</summary>
public static class NodesChangeTrackerExtensions
{
    /// <summary>
    /// Detaches every tracked entry except the bus outbox's <see cref="OutboxState"/>, so a retried attempt starts clean.
    /// MassTransit's scoped bus context adds one OutboxState per scope and adds it again only after it was saved, so
    /// detaching an unsaved one would make the retried OutboxMessage reference a row that was never written.
    /// </summary>
    public static void ResetForRetry(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is not OutboxState)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
