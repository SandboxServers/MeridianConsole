namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Timing both sides of certificate renewal rely on (D-S212). After a renewal, Nodes keeps the
/// presented certificate usable for a short grace window, so an agent whose renewal response was
/// lost can renew again with it. The agent retries a failed renewal well inside that window.
/// </summary>
public static class AgentCertificateRenewalPolicy
{
    /// <summary>Default of Nodes' Mtls:RenewalGraceMinutes. 0 there means immediate revoke.</summary>
    public const int DefaultGraceMinutes = 60;

    /// <summary>Upper bound Nodes accepts for Mtls:RenewalGraceMinutes.</summary>
    public const int MaxGraceMinutes = 1440;

    /// <summary>Default delay before the agent retries a failed renewal. Must stay well below DefaultGraceMinutes.</summary>
    public const int AgentRetryMinutes = 5;
}
