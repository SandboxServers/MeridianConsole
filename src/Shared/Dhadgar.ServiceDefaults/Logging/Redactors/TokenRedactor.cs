using Microsoft.Extensions.Compliance.Redaction;

namespace Dhadgar.ServiceDefaults.Logging.Redactors;

/// <summary>
/// Redacts authentication tokens and API keys with length information.
/// </summary>
/// <remarks>
/// <para>
/// Outputs tokens in the format "[REDACTED-TOKEN:len=N]" where N is the original token length.
/// The length hint helps with debugging (e.g., identifying truncated tokens) while keeping
/// the actual token value completely hidden.
/// </para>
/// <para>
/// Use this redactor for:
/// </para>
/// <list type="bullet">
///   <item>JWT tokens (Bearer tokens)</item>
///   <item>Refresh tokens</item>
///   <item>API keys</item>
///   <item>Session identifiers</item>
///   <item>OAuth access tokens</item>
/// </list>
/// <para>
/// Usage: Register with <see cref="DhadgarDataClassifications.Token"/> or
/// <see cref="DhadgarDataClassifications.ApiKey"/> classifications.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddRedaction(builder =>
/// {
///     builder.SetRedactor&lt;TokenRedactor&gt;(DhadgarDataClassifications.Token);
///     builder.SetRedactor&lt;TokenRedactor&gt;(DhadgarDataClassifications.ApiKey);
/// });
/// </code>
/// </example>
public sealed class TokenRedactor : Redactor
{
    /// <summary>
    /// Prefix for the redacted output.
    /// </summary>
    private const string Prefix = "[REDACTED-TOKEN:len=";

    /// <summary>
    /// Suffix for the redacted output.
    /// </summary>
    private const string Suffix = "]";

    /// <summary>
    /// Gets the length of the redacted output.
    /// </summary>
    /// <param name="input">The token to redact.</param>
    /// <returns>The length of the redacted output string.</returns>
    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        // Format: [REDACTED-TOKEN:len=N]
        // We need to calculate how many digits are in the length
        var lengthDigits = input.Length switch
        {
            0 => 1,
            _ => (int)Math.Floor(Math.Log10(input.Length)) + 1
        };

        return Prefix.Length + lengthDigits + Suffix.Length;
    }

    /// <summary>
    /// Redacts the token.
    /// </summary>
    /// <param name="source">The token to redact.</param>
    /// <param name="destination">The buffer to write the redacted output to.</param>
    /// <returns>The number of characters written to the destination.</returns>
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        var result = $"{Prefix}{source.Length}{Suffix}";
        result.AsSpan().CopyTo(destination);
        return result.Length;
    }
}
