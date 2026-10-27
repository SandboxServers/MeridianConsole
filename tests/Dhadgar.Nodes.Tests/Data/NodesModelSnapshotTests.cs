using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Tests.Data;

public sealed class NodesModelSnapshotTests
{
    // Tests build their schema with EnsureCreated, so nothing else notices a model change
    // that has no migration (P16-ruling-1). No database connection is opened.
    [Fact]
    public void Model_HasNoPendingChanges_AgainstMigrations()
    {
        using var db = new NodesDbContextFactory().CreateDbContext([]);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "NodesDbContext has model changes with no migration; run 'dotnet ef migrations add'.");
    }
}
