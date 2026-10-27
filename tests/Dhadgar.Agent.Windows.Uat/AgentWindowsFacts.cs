namespace Dhadgar.Agent.Windows.Uat;

/// <summary>
/// Constants the driver copies from the Windows agent. Pinned by
/// <c>AgentWindowsFactsTests</c> through InternalsVisibleTo, so a rename on either side fails a test.
/// The driver does not reference the agent assembly at runtime.
/// </summary>
internal static class AgentWindowsFacts
{
    /// <summary>Windows service name (<c>Program.ServiceName</c>).</summary>
    public const string ServiceName = "DhadgarAgent";

    /// <summary>Event log source (<c>Program.EventLogSourceName</c>).</summary>
    public const string EventLogSourceName = "Meridian Console Agent";

    /// <summary>Local machine system store for the agent CA (<c>WindowsCertificateStore.CaStoreName</c>).</summary>
    public const string CaStoreName = "MeridianConsoleAgent";

    /// <summary>Registry key of the agent (<c>AgentRegistry.KeyPath</c>), under HKLM.</summary>
    public const string RegistryKeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>Parent of the agent's key. Cleanup deletes it only when the run created it and it is empty (D-S238).</summary>
    public const string RegistryParentKeyPath = @"SOFTWARE\Meridian Console";

    /// <summary>Registry value holding the enrollment token (<c>AgentRegistry.EnrollmentTokenValueName</c>).</summary>
    public const string EnrollmentTokenValueName = "EnrollmentToken";

    /// <summary>Registry value holding the control plane endpoint, written by the MSI (D-S230).</summary>
    public const string ControlPlaneEndpointValueName = "ControlPlaneEndpoint";

    /// <summary>The MSI's UpgradeCode (<c>Package/@UpgradeCode</c> in <c>Package.wxs</c>), which finds the installed product.</summary>
    public const string MsiUpgradeCode = "8A3B4E6F-1D2C-4A5B-9E0F-7C8D6B5A4E3F";

    /// <summary>The MSI public property that carries the enrollment token (<c>msiexec ENROLLMENT_TOKEN=...</c>).</summary>
    public const string MsiTokenProperty = "ENROLLMENT_TOKEN";

    /// <summary>The MSI public property that carries the control plane endpoint (<c>msiexec CONTROL_PLANE_ENDPOINT=...</c>).</summary>
    public const string MsiEndpointProperty = "CONTROL_PLANE_ENDPOINT";
}
