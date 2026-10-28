using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Stack;

/// <summary>
/// The data reset's SQL: the migrations history is never truncated, every name is quoted, and an unknown database is refused
/// before any connection opens.
/// </summary>
public sealed class LabDataResetTests
{
    [Fact]
    public void TruncateStatement_NeverNamesTheMigrationsHistory()
    {
        string sql = LabDataReset.BuildTruncate(["nodes_a", "__EFMigrationsHistory", "agent_certificates"]);

        Assert.DoesNotContain("__EFMigrationsHistory", sql, StringComparison.Ordinal);
        Assert.Contains("\"nodes_a\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"agent_certificates\"", sql, StringComparison.Ordinal);
        Assert.EndsWith("RESTART IDENTITY CASCADE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncateStatement_QuotesNamesAndDoublesEmbeddedQuotes()
    {
        string sql = LabDataReset.BuildTruncate(["we\"ird; DROP"]);

        Assert.Equal("TRUNCATE TABLE \"we\"\"ird; DROP\" RESTART IDENTITY CASCADE", sql);
    }

    [Fact]
    public async Task Reset_RefusesADatabaseThatIsNotALabDatabase()
    {
        using var vault = new SecretVault();
        var reset = new LabDataReset(new LabConnectionStrings(vault, new LabCredentialRefs()));

        Result<IReadOnlyList<TruncatedDatabase>> result = await reset.ResetAsync(["postgres"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }
}
