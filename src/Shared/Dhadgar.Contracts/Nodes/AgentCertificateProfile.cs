namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// What an agent client certificate looks like. Nodes' CA issues to this profile, Nodes' mTLS
/// validates against it, the Gateway checks the issuer name, and the Windows store finds certs by it.
/// </summary>
public static class AgentCertificateProfile
{
    public const string SpiffeTrustDomain = "meridianconsole.com";
    public const string SpiffeNodeIdPrefix = "spiffe://meridianconsole.com/nodes/";
    public static string SpiffeId(Guid nodeId) => $"{SpiffeNodeIdPrefix}{nodeId:D}";

    /// <summary>O= of the subject; the subject is CN={nodeId:D}, O=MeridianConsole.</summary>
    public const string Organization = "MeridianConsole";

    /// <summary>CN of the agent CA (issuer of every agent client certificate).</summary>
    public const string CaCommonName = "Meridian Console Agent CA";

    public const int ValidityDays = 90;

    /// <summary>The agent renews when fewer than this many days remain.</summary>
    public const int RenewalThresholdDays = 30;

    /// <summary>Gateway → Nodes header carrying the TLS client certificate as base64 DER (D-S25).</summary>
    public const string ForwardedCertificateHeader = "X-Client-Cert";
}
