using Dhadgar.Lab.Data;

namespace Dhadgar.Lab.Tests.Data;

/// <summary>
/// <see cref="ColumnMask"/> decisions for <c>db_query</c>. No database is needed: the decision is a pure function of the
/// database, the base column the value came from, and its Postgres type.
/// </summary>
public sealed class ColumnMaskTests
{
    [Fact]
    public void Sensitive_BaseColumn_Masked()
    {
        Assert.True(ColumnMask.ShouldMask("betterauth", ("session", "token"), "text"));
        Assert.True(ColumnMask.ShouldMask("betterauth", ("account", "accessToken"), "character varying"));
        Assert.True(ColumnMask.ShouldMask("identity", ("refresh_tokens", "TokenHash"), "character varying"));
        Assert.True(ColumnMask.ShouldMask("identity", ("OpenIddictApplications", "ClientSecret"), "text"));
    }

    [Fact]
    public void Alias_SameBaseColumn_Masked()
    {
        // An alias never reaches ShouldMask: the origin is the base column, so the same origin is masked whatever the statement called it.
        (string Table, string Column) origin = ("session", "token");

        Assert.True(ColumnMask.ShouldMask("betterauth", origin, "text"));
        Assert.True(ColumnMask.ShouldMask("betterauth", origin, "character varying"));
        Assert.False(ColumnMask.ShouldMask("betterauth", ("session", "userId"), "text"));
    }

    [Fact]
    public void Expression_InIdentity_TextMasked()
    {
        Assert.True(ColumnMask.ShouldMask("identity", null, "text"));
        Assert.True(ColumnMask.ShouldMask("identity", null, "character varying"));
        Assert.True(ColumnMask.ShouldMask("betterauth", null, "jsonb"));
        Assert.True(ColumnMask.ShouldMask("identity", null, "bytea"));
    }

    [Fact]
    public void Expression_InNodes_NotMasked()
    {
        Assert.False(ColumnMask.ShouldMask("nodes", null, "text"));
        Assert.False(ColumnMask.ShouldMask("nodes", null, "jsonb"));
    }

    [Fact]
    public void OtherColumn_NotMasked()
    {
        Assert.False(ColumnMask.ShouldMask("betterauth", ("session", "userId"), "text"));
        Assert.False(ColumnMask.ShouldMask("identity", ("refresh_tokens", "Id"), "integer"));
        Assert.False(ColumnMask.ShouldMask("identity", null, "integer"));
        Assert.False(ColumnMask.ShouldMask("nodes", ("session", "token"), "text"));
    }
}
