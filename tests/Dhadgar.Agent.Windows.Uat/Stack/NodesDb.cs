using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// Creates Nodes database contexts for the UAT stack's Postgres (P19). Callers dispose the context with
/// <c>await using</c> and read with <c>AsNoTracking()</c>.
/// </summary>
internal sealed class NodesDb
{
    private readonly string _connectionString;

    /// <summary>Creates a factory for one connection string.</summary>
    public NodesDb(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>A new Nodes context. The caller owns it.</summary>
    public NodesDbContext Create()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>().UseNpgsql(_connectionString).Options;
        return new NodesDbContext(options);
    }
}
