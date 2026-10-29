using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.SelfCheck;

/// <summary>
/// The entry point of <c>selfcheck [--only &lt;id&gt;] [--keep] [--recovery]</c> (L55). Exit code 0 only when every lab
/// mutant is <c>caught</c>. A named class, not top-level statements, so the test project can reference this assembly
/// beside the Nodes assembly (whose entry type is also <c>Program</c>).
/// </summary>
public static class SelfCheckProgram
{
    /// <summary>Runs the command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    public static int Main(string[] args)
    {
        Result<SelfCheckOptions> parsed = SelfCheckOptions.Parse(args);
        if (!parsed.IsSuccess)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        // The mutant loop and the recovery check arrive in later steps of L55.
        Console.Error.WriteLine(
            $"selfcheck: not implemented yet (only={parsed.Value!.Only ?? "all"}, keep={parsed.Value.Keep}, recovery={parsed.Value.Recovery})");
        return 2;
    }
}
