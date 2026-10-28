using Dhadgar.Lab.Data;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// The statement scanner behind <c>db_query</c>: one statement only, literals and comments skipped.
/// </summary>
public sealed class SqlStatementScannerTests
{
    [Fact]
    public void Check_TwoStatements_IsRefused()
    {
        Result result = SqlStatementScanner.Check("select 1; drop table x");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_SemicolonInsideLiteral_IsAllowed()
    {
        Assert.True(SqlStatementScanner.Check("select ';'").IsSuccess);
    }

    [Theory]
    [InlineData("select 1;")]
    [InlineData("select 1; -- trailing comment")]
    [InlineData("select 1 -- ; not a statement break\n")]
    [InlineData("select 1 /* ; not a break */")]
    [InlineData("select /* outer /* nested ; */ still comment */ 1")]
    [InlineData("select 'it''s ; fine'")]
    [InlineData("select \"col;name\" from t")]
    public void Check_SingleStatementWithLiteralsOrComments_IsAllowed(string sql)
    {
        Assert.True(SqlStatementScanner.Check(sql).IsSuccess);
    }

    [Theory]
    [InlineData("select 1; select 2")]
    [InlineData("select 1;; select 2")]
    [InlineData("select 1; 'x'")]
    [InlineData("select E'\\''; drop table x; --'")]
    [InlineData("select $$x$$")]
    [InlineData("select 'unterminated")]
    [InlineData("select 1 /* unterminated")]
    public void Check_UnsafeOrAmbiguousSql_IsRefused(string sql)
    {
        Assert.True(SqlStatementScanner.Check(sql).IsFailure);
    }
}
