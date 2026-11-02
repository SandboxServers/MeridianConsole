namespace Dhadgar.Lab.Services;

/// <summary>
/// Formats multi-line values for a Docker <c>--env-file</c>, which holds one <c>KEY=value</c> per line.
/// </summary>
public static class LabEnvValue
{
    /// <summary>
    /// Escapes a PEM as one line: each line break becomes a literal <c>\n</c>. Identity's exchange validator
    /// normalises <c>\n</c> back to a line break, so the value stays one env-file line.
    /// </summary>
    /// <param name="pem">The PEM text; never logged.</param>
    public static string EscapedNewlinePem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);
        return pem.Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Drops every line break from a PEM, keeping the header, body and footer on one line. The BetterAuth exchange
    /// loader (jose <c>importPKCS8</c>) strips whitespace from the body itself, and a literal <c>\n</c> would not be
    /// stripped, so this form is the one BetterAuth accepts from an env file.
    /// </summary>
    /// <param name="pem">The PEM text; never logged.</param>
    public static string SingleLinePem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);
        return pem.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
