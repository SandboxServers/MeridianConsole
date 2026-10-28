using Dhadgar.Contracts.Agents;

namespace Dhadgar.Nodes.Services.CommandSigning;

/// <summary>
/// Signs agent commands with the control plane's P-256 command signing key (C-3, D-09).
/// </summary>
public interface ICommandSigner
{
    /// <summary>
    /// Base64 DER SubjectPublicKeyInfo of the signing key, handed to agents at enrollment.
    /// </summary>
    string PublicKeySpkiBase64 { get; }

    /// <summary>
    /// Key id: lower-case hex SHA-256 of the SubjectPublicKeyInfo.
    /// </summary>
    string KeyId { get; }

    /// <summary>
    /// Loads the signing key, or creates it on first start.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a copy of <paramref name="unsigned"/> with <c>Signature</c> and <c>SigningKeyId</c> set.
    /// </summary>
    /// <param name="unsigned">The envelope to sign; its signature fields are ignored.</param>
    AgentCommandEnvelope Sign(AgentCommandEnvelope unsigned);
}