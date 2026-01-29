using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class CapacityReservationConfiguration : IEntityTypeConfiguration<CapacityReservation>
{
    public void Configure(EntityTypeBuilder<CapacityReservation> builder)
    {
        builder.ToTable("capacity_reservations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReservationToken)
            .IsRequired();

        builder.Property(r => r.RequestedBy)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ServerId)
            .HasMaxLength(100);

        builder.Property(r => r.CorrelationId)
            .HasMaxLength(100);

        builder.Property(r => r.Status)
            .HasConversion<int>();

        // Unique index on reservation token for fast lookups
        builder.HasIndex(r => r.ReservationToken)
            .IsUnique()
            .HasDatabaseName("ix_capacity_reservations_token");

        // Index for finding active reservations by node
        builder.HasIndex(r => new { r.NodeId, r.Status })
            .HasDatabaseName("ix_capacity_reservations_node_status");

        // Index for expiry checks (finding pending reservations that have expired)
        builder.HasIndex(r => new { r.Status, r.ExpiresAt })
            .HasFilter("\"Status\" = 0") // Pending only
            .HasDatabaseName("ix_capacity_reservations_expiry");

        // Index for finding reservations by server ID
        builder.HasIndex(r => r.ServerId)
            .HasFilter("\"ServerId\" IS NOT NULL")
            .HasDatabaseName("ix_capacity_reservations_server_id");

        // Relationship to Node
        builder.HasOne(r => r.Node)
            .WithMany()
            .HasForeignKey(r => r.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
