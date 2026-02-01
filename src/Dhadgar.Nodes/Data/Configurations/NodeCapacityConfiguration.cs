using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class NodeCapacityConfiguration : IEntityTypeConfiguration<NodeCapacity>
{
    public void Configure(EntityTypeBuilder<NodeCapacity> builder)
    {
        builder.ToTable("node_capacities");

        builder.HasKey(c => c.Id);

        // Unique index on NodeId (one capacity record per node)
        builder.HasIndex(c => c.NodeId)
            .IsUnique()
            .HasDatabaseName("ix_node_capacities_node_id");

        // Index for finding nodes with available capacity
        builder.HasIndex(c => new { c.MaxGameServers, c.CurrentGameServers })
            .HasDatabaseName("ix_node_capacities_availability");
    }
}
