using System.Collections.Frozen;

namespace Dhadgar.Lab.Data;

/// <summary>
/// Decides which <c>db_query</c> result cells carry credential material and must be replaced by <see cref="Masked"/>.
/// The decision keys on where a value came from (its base table column, from the catalog), never on the alias the
/// statement gives it, so <c>SELECT token AS t</c> and <c>SELECT token || '' AS x</c> are masked like <c>SELECT token</c>.
/// </summary>
internal static class ColumnMask
{
    /// <summary>The text a masked, non-null cell is replaced with.</summary>
    public const string Masked = "[masked]";

    // (database, table, column), Postgres names, case-sensitive.
    internal static readonly FrozenSet<(string Db, string Table, string Column)> Sensitive = new[]
    {
        ("betterauth", "session", "token"), ("betterauth", "account", "accessToken"), ("betterauth", "account", "refreshToken"),
        ("betterauth", "account", "idToken"), ("betterauth", "account", "password"), ("betterauth", "verification", "value"),
        ("identity", "refresh_tokens", "TokenHash"), ("identity", "OpenIddictTokens", "Payload"),
        ("identity", "OpenIddictTokens", "ReferenceId"), ("identity", "OpenIddictApplications", "ClientSecret"),
    }.ToFrozenSet();

    // Databases whose computed expressions are masked by type, because an expression can carry a secret under any alias.
    internal static readonly FrozenSet<string> ExpressionMaskedDbs = new[] { "betterauth", "identity" }.ToFrozenSet();

    private static readonly FrozenSet<string> ExpressionMaskedTypes = new[]
    {
        "text", "character varying", "character", "bytea", "json", "jsonb",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether a cell is masked. <paramref name="origin"/> is the (table, column) of the base column the value was read
    /// from, or <c>null</c> for an expression. A base column in <see cref="Sensitive"/> is masked whatever its type; an
    /// expression in an <see cref="ExpressionMaskedDbs"/> database is masked when its type is textual or binary; anything
    /// else passes through.
    /// </summary>
    public static bool ShouldMask(string database, (string Table, string Column)? origin, string dataTypeName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(dataTypeName);

        if (origin is { } column)
        {
            return Sensitive.Contains((database, column.Table, column.Column));
        }

        return ExpressionMaskedDbs.Contains(database) && ExpressionMaskedTypes.Contains(dataTypeName);
    }
}
