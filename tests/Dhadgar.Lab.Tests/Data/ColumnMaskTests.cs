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
        Assert.False(ColumnMask.ShouldMask("nodes", null, "name"));
    }

    [Fact]
    public void OtherColumn_NotMasked()
    {
        Assert.False(ColumnMask.ShouldMask("betterauth", ("session", "userId"), "text"));
        Assert.False(ColumnMask.ShouldMask("identity", ("refresh_tokens", "Id"), "integer"));
        Assert.False(ColumnMask.ShouldMask("identity", null, "integer"));
        Assert.False(ColumnMask.ShouldMask("nodes", ("nodes", "Name"), "text"));
    }

    [Theory]
    [InlineData("betterauth", "name")]
    [InlineData("betterauth", "xml")]
    [InlineData("betterauth", "tsvector")]
    [InlineData("betterauth", "\"char\"")]
    [InlineData("betterauth", "text[]")]
    [InlineData("betterauth", "record")]
    [InlineData("betterauth", "session")]
    [InlineData("identity", "name")]
    [InlineData("identity", "xml")]
    [InlineData("identity", "tsvector")]
    [InlineData("identity", "\"char\"")]
    [InlineData("identity", "text[]")]
    [InlineData("identity", "record")]
    [InlineData("identity", "session")]
    public void Expression_NonPassThroughType_Masked(string db, string type)
    {
        Assert.True(ColumnMask.ShouldMask(db, null, type));
    }

    [Theory]
    [InlineData("betterauth", "integer")]
    [InlineData("betterauth", "bigint")]
    [InlineData("betterauth", "boolean")]
    [InlineData("betterauth", "numeric")]
    [InlineData("betterauth", "timestamp with time zone")]
    [InlineData("betterauth", "uuid")]
    [InlineData("identity", "integer")]
    [InlineData("identity", "bigint")]
    [InlineData("identity", "boolean")]
    [InlineData("identity", "numeric")]
    [InlineData("identity", "timestamp with time zone")]
    [InlineData("identity", "uuid")]
    public void Expression_PassThroughType_NotMasked(string db, string type)
    {
        Assert.False(ColumnMask.ShouldMask(db, null, type));
    }

    [Fact]
    public void SensitiveName_BaseColumn_Masked()
    {
        Assert.True(ColumnMask.ShouldMask("nodes", ("session", "token"), "text"));
        Assert.True(ColumnMask.ShouldMask("betterauth", ("account", "access_token"), "text"));
        Assert.True(ColumnMask.ShouldMask("discord", ("guilds", "BotToken"), "character varying"));
        Assert.True(ColumnMask.ShouldMask("identity", ("users", "PasswordHash"), "text"));
        Assert.False(ColumnMask.ShouldMask("betterauth", ("account", "accessTokenExpiresAt"), "timestamp with time zone"));
        Assert.False(ColumnMask.ShouldMask("nodes", ("enrollment_tokens", "UsedAt"), "timestamp with time zone"));
        Assert.False(ColumnMask.ShouldMask("nodes", ("enrollment_tokens", "Id"), "uuid"));
    }

    [Fact]
    public void Nodes_CredentialColumns_Masked()
    {
        Assert.True(ColumnMask.ShouldMask("nodes", ("capacity_reservations", "ReservationToken"), "uuid"));
        Assert.True(ColumnMask.ShouldMask("nodes", ("enrollment_tokens", "TokenHash"), "character varying"));
    }
}
