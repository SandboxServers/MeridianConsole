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

    /// <summary>Registry value holding the enrollment token (<c>AgentRegistry.EnrollmentTokenValueName</c>).</summary>
    public const string EnrollmentTokenValueName = "EnrollmentToken";
}
