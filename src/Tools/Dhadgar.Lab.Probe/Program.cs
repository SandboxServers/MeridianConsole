using System.Text.Json;
using Dhadgar.Contracts.Agents;

namespace Dhadgar.Lab.Probe;

/// <summary>
/// The probe's command line. <see cref="Run"/> holds the dispatch so tests call it without a process.
/// Part 1: only the mode dispatch exists; <c>footprint</c> is wired in the next session (see the worknote).
/// </summary>
public static class ProbeCli
{
    /// <summary>
    /// Runs one probe mode and returns the exit code: 0 on success, 2 on a usage or runtime error.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 0)
        {
            stderr.WriteLine("usage: meridian-lab-probe footprint --json");
            return 2;
        }

        switch (args[0])
        {
            case "footprint":
                return Footprint(args, stdout, stderr);

            case "sink":
                return args.Length == 1 ? SinkHost.Run(stderr) : Usage(stderr, "usage: meridian-lab-probe sink");

            default:
                stderr.WriteLine("unknown mode");
                return 2;
        }
    }

    private static int Footprint(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2 || args[1] != "--json")
        {
            stderr.WriteLine("usage: meridian-lab-probe footprint --json");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            stderr.WriteLine("footprint is Windows-only");
            return 2;
        }

        if (!ReadWindowsFootprint(out ProbeFootprint? footprint, out string? error))
        {
            stderr.WriteLine(error ?? "footprint read failed");
            return 2;
        }

        stdout.WriteLine(JsonSerializer.Serialize(footprint, FootprintJson));
        return 0;
    }

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine(message);
        return 2;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool ReadWindowsFootprint(out ProbeFootprint? footprint, out string? error)
    {
        return FootprintReader.TryRead(out footprint, out error);
    }

    private static readonly JsonSerializerOptions FootprintJson = new() { WriteIndented = false };
}

/// <summary>
/// Process entry point. Writes to the console and returns the exit code from <see cref="ProbeCli.Run"/>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Entry point.
    /// </summary>
    public static int Main(string[] args)
    {
        return ProbeCli.Run(args, Console.Out, Console.Error);
    }
}
