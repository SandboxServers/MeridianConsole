using Dhadgar.Console.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Console.Data.Configurations;

public sealed class ConsoleHistoryEntryConfiguration : IEntityTypeConfiguration<ConsoleHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ConsoleHistoryEntry> builder)
    {
        builder.ToTable("console_history");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Content)
            .IsRequired()
            .HasMaxLength(65535);

        builder.Property(e => e.OutputType)
            .HasConversion<int>();

        // Index for time-based queries
        builder.HasIndex(e => e.Timestamp)
            .HasDatabaseName("ix_console_history_timestamp");

        // Composite index for server + time range queries
        builder.HasIndex(e => new { e.ServerId, e.Timestamp })
            .HasDatabaseName("ix_console_history_server_timestamp");

        // Index for sequence ordering
        builder.HasIndex(e => new { e.ServerId, e.SequenceNumber })
            .HasDatabaseName("ix_console_history_server_sequence");

        // Index for session-based queries
        builder.HasIndex(e => e.SessionId)
            .HasDatabaseName("ix_console_history_session");

        // Index for organization-level queries
        builder.HasIndex(e => e.OrganizationId)
            .HasDatabaseName("ix_console_history_org");
    }
}
