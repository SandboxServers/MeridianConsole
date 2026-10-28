using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Secrets;

/// <summary>
/// Redacts <c>name=value</c> pairs whose name is credential-like (a token, secret, password, key or cookie) in free text, such as
/// a plain HTTP body. It is the one list of credential-like names: the bundle's sensitive-name test reads <see cref="Words"/>.
/// </summary>
internal static partial class SensitiveText
{
    /// <summary>The credential-like names. The pattern in <see cref="Pair"/> matches the same words.</summary>
    internal static readonly string[] Words = ["token", "secret", "password", "passwd", "key", "cookie", "authorization", "credential", "pwd"];

    // The name starts at a run boundary (the lookbehind), so a long run of name characters is scanned once, not once per start position.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.-])(?<name>[A-Za-z0-9_.-]*(token|secret|password|passwd|pwd|key|cookie|authorization|credential)[A-Za-z0-9_.-]*)\s*[=:]\s*(""[^""]*""|[^\s&;,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Pair();

    /// <summary>
    /// Replaces the value of each credential-like pair with <c>[redacted]</c>, keeping the name.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="count">The number of pairs replaced.</param>
    /// <returns>The redacted text, or <c>***redaction-timeout***</c> when the scan runs too long.</returns>
    internal static string Redact(string text, out int count)
    {
        int replaced = 0;
        try
        {
            string redacted = Pair().Replace(text, match =>
            {
                replaced++;
                return match.Groups["name"].Value + "=[redacted]";
            });
            count = replaced;
            return redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            count = 0;
            return "***redaction-timeout***";
        }
    }
}
