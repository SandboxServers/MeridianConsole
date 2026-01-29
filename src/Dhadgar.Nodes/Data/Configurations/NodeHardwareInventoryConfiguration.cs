using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class NodeHardwareInventoryConfiguration : IEntityTypeConfiguration<NodeHardwareInventory>
{
    public void Configure(EntityTypeBuilder<NodeHardwareInventory> builder)
    {
        builder.ToTable("node_hardware_inventories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Hostname)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(h => h.OsVersion)
            .HasMaxLength(200);

        // Store network interfaces as JSONB
        builder.Property(h => h.NetworkInterfaces)
            .HasColumnType("jsonb");

        // Unique index on NodeId (one inventory per node)
        builder.HasIndex(h => h.NodeId)
            .IsUnique()
            .HasDatabaseName("ix_node_hardware_inventories_node_id");
    }
}
