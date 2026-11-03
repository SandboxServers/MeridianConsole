using Dhadgar.Nodes.Data;
using MassTransit;

namespace Dhadgar.Nodes.Messaging;

/// <summary>
/// Shared MassTransit wiring for Nodes. Production (Postgres) and the tests (SQLite) register the same outbox.
/// </summary>
public static class NodesMessaging
{
    /// <summary>
    /// The EF outbox for NodesDbContext. Publishes made before SaveChanges are stored with the entity changes
    /// and delivered after commit. A failed SaveChanges delivers nothing.
    /// </summary>
    /// <param name="x">The MassTransit bus registration configurator.</param>
    /// <param name="provider">Selects the database provider for the outbox tables (for example <c>UsePostgres</c>).</param>
    public static void AddNodesOutbox(IBusRegistrationConfigurator x, Action<IEntityFrameworkOutboxConfigurator> provider)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(provider);

        x.AddEntityFrameworkOutbox<NodesDbContext>(o =>
        {
            provider(o);
            o.UseBusOutbox();
            o.DisableInboxCleanupService();
            o.QueryDelay = TimeSpan.FromSeconds(1);
        });
    }
}
