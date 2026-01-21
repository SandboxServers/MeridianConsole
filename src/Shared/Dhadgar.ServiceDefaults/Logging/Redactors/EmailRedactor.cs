using Microsoft.Extensions.Compliance.Redaction;

namespace Dhadgar.ServiceDefaults.Logging.Redactors;

/// <summary>
/// Redacts email addresses to a constant pattern "***@***.***".
/// </summary>
/// <remarks>
/// <para>
/// This redactor completely obscures email addresses to prevent PII leakage.
/// Unlike partial redaction (e.g., "u***@domain.com"), this approach:
/// </para>
/// <list type="bullet">
///   <item>Hides the domain, preventing identification of corporate/personal accounts</item>
///   <item>Returns a constant length output, preventing length-based inference</item>
///   <item>Provides no information about the original email structure</item>
/// </list>
/// <para>
/// Usage: Register with <see cref="DhadgarDataClassifications.Email"/> classification.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddRedaction(builder =>
/// {
///     builder.SetRedactor&lt;EmailRedactor&gt;(DhadgarDataClassifications.Email);
/// });
/// </code>
/// </example>
public sealed class EmailRedactor : Redactor
{
    /// <summary>
    /// The redacted output for all email addresses.
    /// </summary>
    private const string RedactedEmail = "***@***.***";

    /// <summary>
    /// Gets the length of the redacted output.
    /// </summary>
    /// <param name="input">The input to redact (ignored - output length is constant).</param>
    /// <returns>The constant length of the redacted email pattern.</returns>
    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        return RedactedEmail.Length;
    }

    /// <summary>
    /// Redacts the email address.
    /// </summary>
    /// <param name="source">The email address to redact.</param>
    /// <param name="destination">The buffer to write the redacted output to.</param>
    /// <returns>The number of characters written to the destination.</returns>
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        RedactedEmail.AsSpan().CopyTo(destination);
        return RedactedEmail.Length;
    }
}
