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
                stderr.WriteLine("footprint is not implemented in this build");
                return 2;

            default:
                stderr.WriteLine("unknown mode");
                return 2;
        }
    }
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
