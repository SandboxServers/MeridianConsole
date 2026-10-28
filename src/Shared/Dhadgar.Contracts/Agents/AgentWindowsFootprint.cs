namespace Dhadgar.Contracts.Agents;

/// <summary>
/// The fixed Windows names the agent installs under. The agent, the lab probe and the lab read these; nothing else
/// hard-codes them.
/// </summary>
public static class AgentWindowsFootprint
{
    public const string ServiceName = "DhadgarAgent";
    public const string RegistryKeyPath = @"SOFTWARE\Meridian Console\Agent";   // under HKLM
    public const string EnrollmentTokenValueName = "EnrollmentToken";
    public const string CaStoreName = "MeridianConsoleAgent";                    // LocalMachine
    public const string StateDirectoryRelative = @"Meridian Console\Agent";     // under %ProgramData%
    public const string StateFileName = "agent-state.json";
    public const string EventLogSourceName = "Meridian Console Agent";
}
