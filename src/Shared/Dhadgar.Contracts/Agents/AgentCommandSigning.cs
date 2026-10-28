using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Pure functions that build the byte string signed for an agent command and derive signing key ids.
/// Nodes signs with these; the agent verifies with the same functions, so the layout is pinned by golden-vector tests.
/// </summary>
public static class AgentCommandSigning
{
    /// <summary>Domain separator, the first line of every signing input.</summary>
    public const string Domain = "dhadgar-cmd-v1";

    /// <summary>
    /// UTF-8 encoding that throws on lone surrogates instead of replacing them with U+FFFD, so that
    /// the signed bytes are a unique encoding of the string.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Builds the signing input for an envelope. Equivalent to the field overload with the envelope's values.
    /// </summary>
    /// <remarks>
    /// <c>payloadJson</c> must be the re-serialization of a validated typed record on the Nodes side, never a caller-supplied string. On the agent it is the raw string received.
    /// </remarks>
    /// <param name="envelope">The command envelope.</param>
    /// <returns>UTF-8 bytes of the signing input.</returns>
    /// <exception cref="EncoderFallbackException">A field contains an unpaired surrogate.</exception>
    public static byte[] BuildSigningInput(AgentCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return BuildSigningInput(
            envelope.CommandId,
            envelope.NodeId,
            envelope.OrganizationId,
            envelope.CommandType,
            envelope.IssuedAt,
            envelope.ExpiresAt,
            envelope.InitiatedByUserId,
            envelope.CorrelationId,
            envelope.PayloadJson);
    }

    /// <summary>
    /// Verify-side variant of <see cref="BuildSigningInput(AgentCommandEnvelope)"/>. Use it on envelope fields
    /// received from the network, where bad input is not a programming error.
    /// </summary>
    /// <param name="envelope">The command envelope as received.</param>
    /// <param name="input">UTF-8 bytes of the signing input when the method returns true; otherwise null.</param>
    /// <returns>
    /// False when the envelope or a required field is null, when <c>CommandType</c> or <c>CorrelationId</c>
    /// contains a line break, or when any signed string contains an unpaired surrogate.
    /// </returns>
    public static bool TryBuildSigningInput(AgentCommandEnvelope envelope, [NotNullWhen(true)] out byte[]? input)
    {
        input = null;
        if (envelope is null || envelope.CommandType is null || envelope.PayloadJson is null)
        {
            return false;
        }

        if (ContainsLineBreak(envelope.CommandType) || ContainsLineBreak(envelope.CorrelationId))
        {
            return false;
        }

        try
        {
            input = BuildSigningInput(envelope);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the signing input: UTF-8 of ten fields joined by LF with no trailing newline.
    /// Strings are encoded with a strict UTF-8 encoder, so a lone surrogate throws instead of being replaced.
    /// </summary>
    /// <remarks>
    /// <c>payloadJson</c> must be the re-serialization of a validated typed record on the Nodes side, never a caller-supplied string. On the agent it is the raw string received.
    /// </remarks>
    /// <param name="commandId">Command id, formatted "D".</param>
    /// <param name="nodeId">Target node id, formatted "D".</param>
    /// <param name="organizationId">Owning organization id, formatted "D".</param>
    /// <param name="commandType">Command type; must not contain line breaks.</param>
    /// <param name="issuedAt">Issue time, as Unix milliseconds.</param>
    /// <param name="expiresAt">Expiry time, as Unix milliseconds.</param>
    /// <param name="initiatedByUserId">Initiating user id, formatted "D", or empty.</param>
    /// <param name="correlationId">Correlation id, or empty; must not contain line breaks.</param>
    /// <param name="payloadJson">Payload JSON; its SHA-256 (lower-case hex) is signed.</param>
    /// <returns>UTF-8 bytes of the signing input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commandType"/> or <paramref name="payloadJson"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="commandType"/> or <paramref name="correlationId"/> contains a line break.</exception>
    /// <exception cref="EncoderFallbackException">A string contains an unpaired surrogate.</exception>
    public static byte[] BuildSigningInput(
        Guid commandId, Guid nodeId, Guid organizationId, string commandType,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid? initiatedByUserId,
        string? correlationId, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(payloadJson);
        RejectLineBreaks(commandType, nameof(commandType));
        RejectLineBreaks(correlationId, nameof(correlationId));

        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(payloadJson)));
        var text = string.Join('\n',
            Domain,
            commandId.ToString("D"),
            nodeId.ToString("D"),
            organizationId.ToString("D"),
            commandType,
            issuedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            expiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            initiatedByUserId?.ToString("D") ?? string.Empty,
            correlationId ?? string.Empty,
            payloadHash);
        return StrictUtf8.GetBytes(text);
    }

    /// <summary>
    /// Computes the signing key id: lower-case hex SHA-256 of the DER SubjectPublicKeyInfo.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">DER-encoded SubjectPublicKeyInfo of the signing key.</param>
    /// <returns>Lower-case hex key id.</returns>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    private static bool ContainsLineBreak(string? value) =>
        value is not null && (value.Contains('\n') || value.Contains('\r'));

    private static void RejectLineBreaks(string? value, string paramName)
    {
        if (ContainsLineBreak(value))
        {
            throw new ArgumentException("Value must not contain line breaks.", paramName);
        }
    }
}
