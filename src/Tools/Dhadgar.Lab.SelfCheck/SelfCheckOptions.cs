using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.SelfCheck;

/// <summary>
/// The arguments of <c>selfcheck [--only &lt;id&gt;] [--keep] [--recovery]</c> (L55). Parsing never throws; an unknown or
/// incomplete argument is a <c>[Lab.InvalidArgument]</c> failure.
/// </summary>
/// <param name="Only">A single mutant id to run, or null for every lab mutant.</param>
/// <param name="Keep">Keep the temporary copy instead of deleting it at the end.</param>
/// <param name="Recovery">Run the crash-recovery check (A1-A4) instead of the mutant loop.</param>
public sealed record SelfCheckOptions(string? Only, bool Keep, bool Recovery)
{
    /// <summary>The verb that starts the command line; accepted only as the first argument.</summary>
    public const string Verb = "selfcheck";

    /// <summary>Parses the command line. A leading <c>selfcheck</c> verb is optional.</summary>
    /// <param name="args">The arguments after the program name.</param>
    public static Result<SelfCheckOptions> Parse(IReadOnlyList<string> args)
    {
        int i = args.Count > 0 && string.Equals(args[0], Verb, StringComparison.Ordinal) ? 1 : 0;
        string? only = null;
        bool keep = false;
        bool recovery = false;

        for (; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--only":
                    if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return Result<SelfCheckOptions>.Failure("[Lab.InvalidArgument] --only needs a mutant id");
                    }

                    only = args[++i];
                    break;
                case "--keep":
                    keep = true;
                    break;
                case "--recovery":
                    recovery = true;
                    break;
                default:
                    return Result<SelfCheckOptions>.Failure($"[Lab.InvalidArgument] unknown argument '{args[i]}'");
            }
        }

        if (recovery && only is not null)
        {
            return Result<SelfCheckOptions>.Failure("[Lab.InvalidArgument] --recovery runs its own check; it does not take --only");
        }

        return Result<SelfCheckOptions>.Success(new SelfCheckOptions(only, keep, recovery));
    }
}
