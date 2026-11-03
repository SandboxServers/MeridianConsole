using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dhadgar.Nodes.Tests.Data;

[Collection("Nodes Integration")]
public sealed class NodesDatabaseTests
{
    // Never connected to: the strategy is inspected, not used. Port 1 is closed by design.
    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=nr1;Username=nr1;Password=nr1";

    [Fact]
    public void Configure_UsesNodesRetryingStrategy()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<NodesDbContext>();
        NodesDatabase.Configure(builder, UnreachableConnectionString);

        // Act
        using var context = new NodesDbContext(builder.Options);
        var strategy = context.Database.CreateExecutionStrategy();

        // Assert
        Assert.IsType<NodesRetryingExecutionStrategy>(strategy);
        Assert.True(strategy.RetriesOnFailure);
    }

    [Fact]
    public void NodesHost_DbContext_RetriesOnFailure()
    {
        // Arrange - the production registration from Program.cs, not the InMemory replacement
        using var factory = new NodesWebApplicationFactory { UseProductionDbContext = true };
        factory.Settings["ConnectionStrings:Postgres"] = UnreachableConnectionString;

        // Act - resolving the context builds the host and opens no connection
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var strategy = context.Database.CreateExecutionStrategy();

        // Assert
        Assert.IsType<NodesRetryingExecutionStrategy>(strategy);
        Assert.True(strategy.RetriesOnFailure);
    }

    [Fact]
    public void IsConnectionFailure_IoFailure_True_ServerError_False()
    {
        // Client-side I/O failure: the pool may hold a dead connection, so the strategy clears it
        Assert.True(NodesRetryingExecutionStrategy.IsConnectionFailure(
            new NpgsqlException("x", new EndOfStreamException())));

        // Server-side errors (serialization failure, deadlock) are retried without clearing the pool
        Assert.False(NodesRetryingExecutionStrategy.IsConnectionFailure(
            new PostgresException("serialization", "ERROR", "ERROR", "40001")));
        Assert.False(NodesRetryingExecutionStrategy.IsConnectionFailure(new InvalidOperationException()));
    }
}
