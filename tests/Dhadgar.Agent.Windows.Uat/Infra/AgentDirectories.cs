namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>Machine-wide directories the agent uses. Undo and the probe handle them by kind, not by a free path.</summary>
internal static class AgentDirectories
{
    /// <summary><c>%ProgramData%\Meridian Console\Agent</c>.</summary>
    public static string MachineAgentDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Meridian Console", "Agent");

    /// <summary>True when <paramref name="path"/> is the machine agent directory itself.</summary>
    public static bool IsMachineAgentDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var machine = Path.GetFullPath(MachineAgentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full, machine, StringComparison.OrdinalIgnoreCase);
    }
}
