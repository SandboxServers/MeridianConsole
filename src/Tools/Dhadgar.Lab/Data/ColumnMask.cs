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
        ("nodes", "enrollment_tokens", "TokenHash"), ("nodes", "capacity_reservations", "ReservationToken"),
    }.ToFrozenSet();

    // Databases whose computed expressions are masked by type, because an expression can carry a secret under any alias.
    internal static readonly FrozenSet<string> ExpressionMaskedDbs = new[] { "betterauth", "identity" }.ToFrozenSet();

    // Types an unattributed value may keep: numbers, booleans, dates and times, uuid (D-ML160). Npgsql DataTypeName spelling.
    internal static readonly FrozenSet<string> PassThroughTypes = new[]
    {
        "smallint", "integer", "bigint", "real", "double precision", "numeric", "boolean", "date",
        "time without time zone", "time with time zone", "timestamp without time zone", "timestamp with time zone",
        "interval", "uuid",
    }.ToFrozenSet(StringComparer.Ordinal);

    // A base column whose real name carries one of these is credential-like in every database (fail closed on schema drift).
    private static readonly string[] SensitiveNameFragments = ["token", "secret", "password", "hash", "payload"];

    /// <summary>
    /// Whether a cell is masked. <paramref name="origin"/> is the (table, column) of the base column the value was read
    /// from, or <c>null</c> for an expression. A base column in <see cref="Sensitive"/> is masked whatever its type; a base
    /// column whose real name carries a credential fragment is masked unless its type is in <see cref="PassThroughTypes"/>;
    /// an expression in an <see cref="ExpressionMaskedDbs"/> database is masked unless its type is in <see cref="PassThroughTypes"/>.
    /// </summary>
    public static bool ShouldMask(string database, (string Table, string Column)? origin, string dataTypeName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(dataTypeName);

        if (origin is { } column)
        {
            if (Sensitive.Contains((database, column.Table, column.Column)))
            {
                return true;
            }

            return IsSensitiveName(column.Column) && !PassThroughTypes.Contains(dataTypeName);
        }

        return ExpressionMaskedDbs.Contains(database) && !PassThroughTypes.Contains(dataTypeName);
    }

    internal static bool IsSensitiveName(string column)
    {
        foreach (string fragment in SensitiveNameFragments)
        {
            if (column.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
