using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class AgentCertificateConfiguration : IEntityTypeConfiguration<AgentCertificate>
{
    public void Configure(EntityTypeBuilder<AgentCertificate> builder)
    {
        builder.ToTable("agent_certificates");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Thumbprint)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.SerialNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.RevocationReason)
            .HasMaxLength(500);

        // Unique index on thumbprint for fast certificate validation
        builder.HasIndex(c => c.Thumbprint)
            .IsUnique()
            .HasDatabaseName("ix_agent_certificates_thumbprint");

        // Index for finding active certificates by node
        builder.HasIndex(c => new { c.NodeId, c.IsRevoked })
            .HasDatabaseName("ix_agent_certificates_node_active");

        // Index for certificate expiration monitoring
        builder.HasIndex(c => c.NotAfter)
            .HasDatabaseName("ix_agent_certificates_expiry");
    }
}
