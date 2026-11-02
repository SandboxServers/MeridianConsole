using System.ComponentModel;
using System.Diagnostics;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// The one helper that builds a lab child process. It uses <c>ArgumentList</c> only, never a shell, redirects both output
/// streams, and removes <c>MERIDIAN_LAB_TOKEN</c> from the child environment. The caller owns the returned process.
/// </summary>
internal static class LabProcessStart
{
    /// <summary>
    /// Builds an unstarted process for the spec. Returns <c>[Lab.InvalidArgument]</c> for an empty file name.
    /// </summary>
    public static Result<Process> Create(LabProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.FileName))
        {
            return Result<Process>.Failure("[Lab.InvalidArgument] process file name is empty");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        StripToken(startInfo);

        foreach (KeyValuePair<string, string> pair in spec.Environment)
        {
            if (!IsTokenName(pair.Key))
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    /// <summary>
    /// Starts an unstarted process. Returns <c>[Lab.Process]</c> when the OS refuses the start.
    /// </summary>
    public static Result StartProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (!process.Start())
            {
                return Result.Failure("[Lab.Process] the process did not start");
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return Result.Failure("[Lab.Process] the process could not be started");
        }
    }

    /// <summary>
    /// Removes every <c>MERIDIAN_LAB_TOKEN</c> key (any case) from the child environment of <paramref name="info"/>. Every lab
    /// child is built through this helper (the three builders call it too), so a token never reaches a child (H3).
    /// </summary>
    /// <param name="info">The start info to clean; its environment is changed in place.</param>
    internal static void StripToken(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        foreach (string key in info.Environment.Keys.Where(IsTokenName).ToList())
        {
            _ = info.Environment.Remove(key);
        }
    }

    private static bool IsTokenName(string name) =>
        string.Equals(name, LabDefaults.TokenEnvVar, StringComparison.OrdinalIgnoreCase);
}
