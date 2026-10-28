namespace Dhadgar.Lab;

/// <summary>
/// Fixed names, paths and limits shared by the daemon, the CLI and the tests.
/// </summary>
public static class LabDefaults
{
    public const int DaemonPort = 8789;
    public const string TokenEnvVar = "MERIDIAN_LAB_TOKEN";
    public const string HomeEnvVar = "MERIDIAN_LAB_HOME";          // overrides LabPaths.Root (tests)
    public const string WindowsContainersTestsEnvVar = "MERIDIAN_LAB_WINDOWS_CONTAINERS";
    public const string ContainerPrefix = "meridian-lab-";
    public const string ContainerLabel = "meridian-lab";            // value "1"
    public const string NetworkName = "meridian-lab-net";
    public const int MinTokenBytes = 32;
    public const string McpPath = "/mcp";
    public const string StatusPath = "/status";
}
