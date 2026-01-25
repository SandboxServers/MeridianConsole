using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Compliance.Redaction;

namespace Dhadgar.ServiceDefaults.Logging.Redactors;

/// <summary>
/// Redacts credentials from connection strings while preserving host and database information.
/// </summary>
/// <remarks>
/// <para>
/// This redactor parses common connection string formats and removes sensitive credentials
/// while keeping diagnostic information visible. Output format:
/// "Host=xxx;Database=xxx;[CREDENTIALS-REDACTED]"
/// </para>
/// <para>
/// Supported connection string formats:
/// </para>
/// <list type="bullet">
///   <item>PostgreSQL: Host=...;Database=...;Username=...;Password=...</item>
///   <item>SQL Server: Server=...;Database=...;User Id=...;Password=...</item>
///   <item>Generic: Host/Server + Database/Initial Catalog + credential pairs</item>
/// </list>
/// <para>
/// Redacted components (case-insensitive):
/// </para>
/// <list type="bullet">
///   <item>Password, Pwd</item>
///   <item>Username, User, User Id, Uid</item>
///   <item>Integrated Security, Trusted_Connection</item>
/// </list>
/// <para>
/// Usage: Register with <see cref="DhadgarDataClassifications.ConnectionString"/> classification.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddRedaction(builder =>
/// {
///     builder.SetRedactor&lt;ConnectionStringRedactor&gt;(DhadgarDataClassifications.ConnectionString);
/// });
///
/// // Input:  "Host=localhost;Database=mydb;Username=admin;Password=secret123"
/// // Output: "Host=localhost;Database=mydb;[CREDENTIALS-REDACTED]"
/// </code>
/// </example>
public sealed partial class ConnectionStringRedactor : Redactor
{
    /// <summary>
    /// Suffix indicating credentials have been redacted.
    /// </summary>
    private const string CredentialsRedactedSuffix = "[CREDENTIALS-REDACTED]";

    /// <summary>
    /// Fully redacted fallback for unparseable connection strings.
    /// </summary>
    private const string FullyRedacted = "[CONNECTION-STRING-REDACTED]";

    /// <summary>
    /// Regex pattern to match host/server component.
    /// </summary>
    [GeneratedRegex(@"(?:Host|Server|Data Source)\s*=\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HostPattern();

    /// <summary>
    /// Regex pattern to match database component.
    /// </summary>
    [GeneratedRegex(@"(?:Database|Initial Catalog)\s*=\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DatabasePattern();

    /// <summary>
    /// Regex pattern to match port component.
    /// </summary>
    [GeneratedRegex(@"Port\s*=\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PortPattern();

    /// <summary>
    /// Gets the length of the redacted output.
    /// </summary>
    /// <param name="input">The connection string to redact.</param>
    /// <returns>The length of the redacted output string.</returns>
    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        // We need to parse the string to know the exact output length
        // This is slightly inefficient but necessary for accurate length calculation
        var redacted = RedactConnectionString(input.ToString());
        return redacted.Length;
    }

    /// <summary>
    /// Redacts the connection string.
    /// </summary>
    /// <param name="source">The connection string to redact.</param>
    /// <param name="destination">The buffer to write the redacted output to.</param>
    /// <returns>The number of characters written to the destination.</returns>
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        var redacted = RedactConnectionString(source.ToString());
        redacted.AsSpan().CopyTo(destination);
        return redacted.Length;
    }

    /// <summary>
    /// Performs the actual redaction of a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to redact.</param>
    /// <returns>The redacted connection string.</returns>
    private static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return FullyRedacted;
        }

        var builder = new StringBuilder();

        // Extract host/server
        var hostMatch = HostPattern().Match(connectionString);
        if (hostMatch.Success)
        {
            builder.Append("Host=");
            builder.Append(hostMatch.Groups[1].Value.Trim());
            builder.Append(';');
        }

        // Extract port (if present)
        var portMatch = PortPattern().Match(connectionString);
        if (portMatch.Success)
        {
            builder.Append("Port=");
            builder.Append(portMatch.Groups[1].Value.Trim());
            builder.Append(';');
        }

        // Extract database
        var dbMatch = DatabasePattern().Match(connectionString);
        if (dbMatch.Success)
        {
            builder.Append("Database=");
            builder.Append(dbMatch.Groups[1].Value.Trim());
            builder.Append(';');
        }

        // If we couldn't extract any identifiable information, return fully redacted
        if (builder.Length == 0)
        {
            return FullyRedacted;
        }

        builder.Append(CredentialsRedactedSuffix);
        return builder.ToString();
    }
}
