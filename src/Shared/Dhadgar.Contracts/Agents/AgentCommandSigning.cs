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
    /// Builds the signing input for an envelope. Equivalent to the field overload with the envelope's values.
    /// </summary>
    /// <param name="envelope">The command envelope.</param>
    /// <returns>UTF-8 bytes of the signing input.</returns>
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
    /// Builds the signing input: UTF-8 of ten fields joined by LF with no trailing newline.
    /// </summary>
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
    public static byte[] BuildSigningInput(
        Guid commandId, Guid nodeId, Guid organizationId, string commandType,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid? initiatedByUserId,
        string? correlationId, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(payloadJson);
        RejectLineBreaks(commandType, nameof(commandType));
        RejectLineBreaks(correlationId, nameof(correlationId));

        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
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
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Computes the signing key id: lower-case hex SHA-256 of the DER SubjectPublicKeyInfo.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">DER-encoded SubjectPublicKeyInfo of the signing key.</param>
    /// <returns>Lower-case hex key id.</returns>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    private static void RejectLineBreaks(string? value, string paramName)
    {
        if (value is not null && (value.Contains('\n') || value.Contains('\r')))
        {
            throw new ArgumentException("Value must not contain line breaks.", paramName);
        }
    }
}
