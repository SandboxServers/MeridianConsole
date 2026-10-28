using Dhadgar.Lab.Cli;

namespace Dhadgar.Lab;

/// <summary>
/// Entry point for the meridian-lab tool. Dev and lab only; never deployed.
/// </summary>
public static class LabProgram
{
    public static Task<int> Main(string[] args) => LabCli.RunAsync(args);
}
