using Dhadgar.Console.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Console.Data.Configurations;

public sealed class CommandAuditLogConfiguration : IEntityTypeConfiguration<CommandAuditLog>
{
    public void Configure(EntityTypeBuilder<CommandAuditLog> builder)
    {
        builder.ToTable("command_audit_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Username)
            .HasMaxLength(200);

        builder.Property(e => e.Command)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(e => e.BlockReason)
            .HasMaxLength(500);

        builder.Property(e => e.ClientIpHash)
            .HasMaxLength(64);

        builder.Property(e => e.ConnectionId)
            .HasMaxLength(100);

        builder.Property(e => e.ResultStatus)
            .HasConversion<int>();

        // Index for server queries
        builder.HasIndex(e => e.ServerId)
            .HasDatabaseName("ix_command_audit_server");

        // Index for user queries
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_command_audit_user");

        // Index for time-based queries
        builder.HasIndex(e => e.ExecutedAt)
            .HasDatabaseName("ix_command_audit_executed_at");

        // Index for organization-level queries
        builder.HasIndex(e => e.OrganizationId)
            .HasDatabaseName("ix_command_audit_org");

        // Composite index for server + time range queries
        builder.HasIndex(e => new { e.ServerId, e.ExecutedAt })
            .HasDatabaseName("ix_command_audit_server_time");

        // Index for blocked commands analysis
        builder.HasIndex(e => e.WasAllowed)
            .HasFilter("\"WasAllowed\" = false")
            .HasDatabaseName("ix_command_audit_blocked");
    }
}
