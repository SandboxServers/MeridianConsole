using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Finds agent certificates by the profile Nodes issues them under (<see cref="AgentCertificateProfile"/>),
/// so the Windows store and other stores match one definition (D-S219, D-S235).
/// </summary>
/// <remarks>
/// Public because the platform stores live in another assembly (Dhadgar.Agent.Windows) and Core does not
/// expose its internals to it.
/// </remarks>
public static class AgentCertificateMatcher
{
    /// <summary>
    /// True when <paramref name="certificate"/> is usable at <paramref name="now"/>, using the one validity rule
    /// shared with enrollment and renewal (<see cref="AgentCertificateIdentity.IsWithinValidity"/>).
    /// </summary>
    public static bool IsWithinValidity(X509Certificate2 certificate, DateTimeOffset now) =>
        AgentCertificateIdentity.IsWithinValidity(certificate, now);

    /// <summary>
    /// True when <paramref name="certificate"/> is an agent client certificate by identity: issued under the agent CA
    /// name and carrying a Nodes SPIFFE URI SAN. Validity time is not checked.
    /// </summary>
    public static bool IsAgentClientIdentity(X509Certificate2 certificate) =>
        string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
            AgentCertificateProfile.CaCommonName, StringComparison.Ordinal)
        && AgentCertificateIdentity.ReadUriNames(certificate)
            .Any(uri => uri.StartsWith(AgentCertificateProfile.SpiffeNodeIdPrefix, StringComparison.Ordinal));

    /// <summary>
    /// True when <paramref name="certificate"/> is an agent client certificate that is also valid at
    /// <paramref name="now"/> (<see cref="IsAgentClientIdentity"/> and <see cref="IsWithinValidity"/>).
    /// </summary>
    public static bool IsAgentClientCertificate(X509Certificate2 certificate, DateTimeOffset now) =>
        IsAgentClientIdentity(certificate) && IsWithinValidity(certificate, now);

    /// <summary>
    /// True when <paramref name="certificate"/> is the agent CA certificate (subject simple name is the CA common name).
    /// </summary>
    public static bool IsAgentCa(X509Certificate2 certificate) =>
        string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
            AgentCertificateProfile.CaCommonName, StringComparison.Ordinal);
}
