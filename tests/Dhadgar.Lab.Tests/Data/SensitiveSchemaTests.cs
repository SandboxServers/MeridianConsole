using System.Text.RegularExpressions;
using Dhadgar.Lab.Data;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// Pins the Identity and Nodes <see cref="ColumnMask.Sensitive"/> entries to their EF model snapshots. BetterAuth entries are checked live (KG-W7-6).
/// </summary>
public sealed class SensitiveSchemaTests
{
    [Fact]
    public void SensitiveEntries_MatchEfSnapshots()
    {
        string root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identity"] = File.ReadAllText(Path.Combine(root, "src", "Dhadgar.Identity", "Data", "Migrations", "IdentityDbContextModelSnapshot.cs")),
            ["nodes"] = File.ReadAllText(Path.Combine(root, "src", "Dhadgar.Nodes", "Data", "Migrations", "NodesDbContextModelSnapshot.cs")),
        };

        foreach ((string db, string table, string column) in ColumnMask.Sensitive)
        {
            if (!snapshots.TryGetValue(db, out string? snapshot))
            {
                continue;
            }

            string label = $"{db}.{table}.{column}";
            string[] chunks = snapshot.Split("modelBuilder.Entity(");
            string[] matching = chunks.Where(c => c.Contains($"b.ToTable(\"{table}\",", StringComparison.Ordinal)).ToArray();
            Assert.True(matching.Length == 1, $"{label}: table is not in the EF snapshot");
            Assert.True(
                Regex.IsMatch(matching[0], @"b\.Property<[^>]+>\(""" + Regex.Escape(column) + @"""\)"),
                $"{label}: column is not in the EF snapshot");
        }
    }
}
