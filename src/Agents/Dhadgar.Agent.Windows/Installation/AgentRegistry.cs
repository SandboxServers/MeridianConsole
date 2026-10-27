namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Registry location the installer and the agent share (D-S232). Internal so the UAT driver pins its own copies
/// to these values through InternalsVisibleTo instead of a public surface (A1).
/// </summary>
internal static class AgentRegistry
{
    /// <summary>Registry path under HKLM where agent configuration is stored.</summary>
    internal const string KeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>Registry value name for the enrollment token (REG_SZ).</summary>
    internal const string EnrollmentTokenValueName = "EnrollmentToken";

    /// <summary>Registry value name for the control plane endpoint (REG_SZ), written by the installer (D-S230).</summary>
    internal const string ControlPlaneEndpointValueName = "ControlPlaneEndpoint";
}
