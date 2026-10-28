using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I3 over fake query results and temporary TOML folders: a check returning rows is a violation, a failing check does not stop the others, and <c>soak.toml</c> is skipped.
/// </summary>
public sealed class I3DbChecksInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0.AddMinutes(-5), T0, null, null);

    private const string NodesToml = """
        schema = 1
        service = "nodes"

        [[check]]
        id = "node-without-cert"
        database = "nodes"
        text = "every enrolled node has exactly one active certificate"
        sql = "select 1"
        """;

    [Fact]
    public async Task I3_CheckReturnsRows_Violation_WithFirstFiveRowsAsEvidence()
    {
        using var folder = new TempFolder();
        folder.Write("nodes.toml", NodesToml);
        using var vault = new SecretVault();
        var check = new I3DbChecksInvariant(folder.Path, Fake(rows: 7), new Redactor(vault));

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);

        Assert.Equal("I3", violation.InvariantId);
        Assert.Equal("nodes", violation.Service);
        Assert.Equal("node-without-cert", violation.CheckId);
        Assert.Equal("every enrolled node has exactly one active certificate: 7 row(s)", violation.Detail);
        Assert.Equal(I3DbChecksInvariant.EvidenceRows, violation.Evidence!.Value.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task I3_StackNotUp_SkipsEachDatabaseOnce()
    {
        using var folder = new TempFolder();
        folder.Write("nodes.toml", NodesToml);
        using var vault = new SecretVault();
        int calls = 0;
        InvariantSqlQuery counting = (_, _, _) =>
        {
            calls++;
            return Task.FromResult(Result<DbQueryResult>.Success(new DbQueryResult([], [], 0, false)));
        };
        InvariantDbGate gate = (_, _) => Task.FromResult(Result<DbApplicability>.Success(DbApplicability.StackNotUp));
        var check = new I3DbChecksInvariant(folder.Path, counting, new Redactor(vault), gate);

        Result<InvariantOutcome> result = await check.CheckWithSkipsAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Violations);
        Assert.Equal(0, calls);
        InvariantSkip skip = Assert.Single(result.Value.Skipped);
        Assert.Equal("db:nodes", skip.Source);
    }

    [Fact]
    public async Task I3_CheckReturnsNoRows_NoViolation()
    {
        using var folder = new TempFolder();
        folder.Write("nodes.toml", NodesToml);
        using var vault = new SecretVault();
        var check = new I3DbChecksInvariant(folder.Path, Fake(rows: 0), new Redactor(vault));

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task I3_OneCheckFails_OthersStillRun_ResultIsFailure()
    {
        using var folder = new TempFolder();
        folder.Write("nodes.toml", NodesToml + "\n\n[[check]]\nid = \"second\"\ndatabase = \"nodes\"\nsql = \"select 2\"\n");
        using var vault = new SecretVault();
        int calls = 0;
        InvariantSqlQuery query = (_, sql, _) =>
        {
            calls++;
            return sql == "select 1"
                ? Task.FromResult(Result<DbQueryResult>.Failure("[Lab.Db] timeout"))
                : Task.FromResult(Result<DbQueryResult>.Success(new DbQueryResult([], [], 0, false)));
        };
        var check = new I3DbChecksInvariant(folder.Path, query, new Redactor(vault));

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ParseChecks_SchemaNotOne_IsFailure()
    {
        Result<IReadOnlyList<DbCheck>> result = I3DbChecksInvariant.ParseChecks("nodes.toml", NodesToml.Replace("schema = 1", "schema = 2", StringComparison.Ordinal));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void LoadChecks_SkipsSoakToml()
    {
        using var folder = new TempFolder();
        folder.Write("nodes.toml", NodesToml);
        folder.Write("soak.toml", "this is not toml [[[");

        Result<IReadOnlyList<DbCheck>> result = I3DbChecksInvariant.LoadChecks(folder.Path);

        DbCheck check = Assert.Single(result.Value!);
        Assert.Equal("nodes", check.Service);
        Assert.Equal("node-without-cert", check.CheckId);
    }

    private static InvariantSqlQuery Fake(int rows) =>
        (_, _, _) =>
        {
            var list = new List<Dictionary<string, JsonElement>>();
            for (int i = 0; i < rows; i++)
            {
                list.Add(new Dictionary<string, JsonElement> { ["Id"] = JsonSerializer.SerializeToElement(i) });
            }

            return Task.FromResult(Result<DbQueryResult>.Success(new DbQueryResult(["Id"], list, list.Count, false)));
        };

    /// <summary>A temporary folder that is deleted on dispose.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lab-i3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string name, string content) => File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
