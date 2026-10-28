using System.Text.Json;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <c>db_query</c> validation that needs no database: unknown databases and bad row limits are refused before any connection.
/// </summary>
public sealed class LabDbTests
{
    [Fact]
    public async Task Query_PostgresDatabase_IsInvalidArgument()
    {
        using var vault = new SecretVault();
        var db = new LabDb(new LabConnectionStrings(vault, new LabCredentialRefs()));

        Result<DbQueryResult> result = await db.QueryAsync("postgres", "select 1", new Dictionary<string, JsonElement>(), 100, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_RowLimitAboveCap_IsInvalidArgument()
    {
        using var vault = new SecretVault();
        var db = new LabDb(new LabConnectionStrings(vault, new LabCredentialRefs()));

        Result<DbQueryResult> result = await db.QueryAsync("nodes", "select 1", new Dictionary<string, JsonElement>(), 501, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ParameterNameWithPunctuation_IsInvalidArgument()
    {
        using var vault = new SecretVault();
        var db = new LabDb(new LabConnectionStrings(vault, new LabCredentialRefs()));
        var parameters = new Dictionary<string, JsonElement> { ["x; drop"] = JsonSerializer.SerializeToElement("v") };

        Result<DbQueryResult> result = await db.QueryAsync("nodes", "select @x", parameters, 10, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }
}
