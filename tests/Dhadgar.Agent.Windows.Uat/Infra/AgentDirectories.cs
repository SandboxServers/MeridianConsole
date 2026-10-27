namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>Machine-wide directories the agent uses. Undo and the probe handle them by kind, not by a free path.</summary>
internal static class AgentDirectories
{
    /// <summary><c>%ProgramData%\Meridian Console\Agent</c>.</summary>
    public static string MachineAgentDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meridian Console", "Agent");

    /// <summary><c>%ProgramFiles%\Meridian Console</c>, the parent of the MSI's install folder (P23).</summary>
    public static string MachineProgramFilesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Meridian Console");

    /// <summary><c>%ProgramFiles%\Meridian Console\Agent</c>, the MSI's install folder (P23).</summary>
    public static string MachineProgramFilesAgentDirectory => Path.Combine(MachineProgramFilesDirectory, "Agent");

    /// <summary>True when <paramref name="path"/> is the machine agent directory itself.</summary>
    public static bool IsMachineAgentDirectory(string path) => IsSameDirectory(path, MachineAgentDirectory);

    /// <summary>True when <paramref name="path"/> is <c>%ProgramFiles%\Meridian Console</c> itself (the MSI's parent folder).</summary>
    public static bool IsMachineProgramFilesDirectory(string path) => IsSameDirectory(path, MachineProgramFilesDirectory);

    /// <summary>True when <paramref name="path"/> is a machine directory the cleanup engine may delete by kind.</summary>
    public static bool IsMachineDirectory(string path) => IsMachineAgentDirectory(path) || IsMachineProgramFilesDirectory(path);

    private static bool IsSameDirectory(string path, string machinePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var machine = Path.GetFullPath(machinePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full, machine, StringComparison.OrdinalIgnoreCase);
    }
}
