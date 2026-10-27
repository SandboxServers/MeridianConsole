using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Services;

/// <summary>Whether an agent certificate row may still authenticate or renew (D-S212).</summary>
public static class AgentCertificateState
{
    /// <summary>
    /// True when the row may still authenticate or renew. The only place that decides this. A row is usable
    /// when it is not revoked, or when it was superseded by a renewal and <paramref name="utcNow"/> is before
    /// its <see cref="AgentCertificate.GraceUntil"/>.
    /// </summary>
    public static bool IsUsable(AgentCertificate certificate, DateTime utcNow) =>
        !certificate.IsRevoked || (certificate.GraceUntil is { } graceUntil && utcNow < graceUntil);

    /// <summary>Hard revoke: no grace. Keeps the first RevokedAt and reason if already revoked.</summary>
    public static void RevokeWithoutGrace(AgentCertificate certificate, DateTime utcNow, string reason)
    {
        if (!certificate.IsRevoked)
        {
            certificate.IsRevoked = true;
            certificate.RevokedAt = utcNow;
            certificate.RevocationReason = reason;
        }

        certificate.GraceUntil = null;
    }
}
