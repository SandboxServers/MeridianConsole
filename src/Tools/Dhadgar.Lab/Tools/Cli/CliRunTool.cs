using System.Diagnostics;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools.Pool;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Cli;

/// <summary>
/// Arguments of <c>cli_run</c>. <c>member</c> is a pool member id (<c>m:&lt;id&gt;</c> is accepted too); <c>args</c> is the
/// CLI argument list, each item its own argument, whose first item must be in the C10 verb allowlist.
/// </summary>
public sealed record CliRunArgs(string Member, IReadOnlyList<string> Args);

/// <summary>
/// Result of <c>cli_run</c>: the exit code, the redacted output and the wall time in milliseconds.
/// </summary>
public sealed record CliRunResult(int ExitCode, string Stdout, string Stderr, long DurationMs);

/// <summary>
/// The C10 CLI allowlist (L37). Only these verbs reach the CLI, and only the listed subcommands behind them.
/// No subcommand that prints a secret is listed: <c>nodes create-token</c> and <c>nodes list-tokens</c> print or
/// reveal enrollment tokens, and the <c>config</c> verb has no safe subcommand in this CLI (it has no subcommands).
/// </summary>
public static class CliAllowlist
{
    /// <summary>The longest single argument accepted.</summary>
    public const int MaxArgLength = 256;

    /// <summary>The most arguments accepted in one call.</summary>
    public const int MaxArgCount = 32;

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> SafeSubcommands =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["auth"] = Set("login", "status", "logout"),
            ["nodes"] = Set("list", "get", "update", "decommission", "maintenance"),
            ["notifications"] = Set("logs", "test"),
            ["discord"] = Set("status", "channels"),
            ["config"] = Set(),
        };

    /// <summary>Flags allowed as the only argument.</summary>
    public static IReadOnlySet<string> Flags { get; } = Set("--help", "--version");

    /// <summary>The verbs allowed as the first argument.</summary>
    public static IReadOnlySet<string> Verbs => SafeSubcommands.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Checks an argument list. Returns <c>[Lab.InvalidArgument]</c> for an empty list, a control character, an
    /// oversized argument, a verb outside the allowlist, or a subcommand outside the safe list for its verb.
    /// </summary>
    public static Result Check(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Result.Failure("[Lab.InvalidArgument] args must name a CLI verb");
        }

        if (args.Count > MaxArgCount)
        {
            return Result.Failure("[Lab.InvalidArgument] too many CLI arguments");
        }

        foreach (string arg in args)
        {
            if (arg is null || arg.Length == 0 || arg.Length > MaxArgLength || arg.Any(char.IsControl))
            {
                return Result.Failure("[Lab.InvalidArgument] CLI arguments must be short text with no control characters");
            }
        }

        string verb = args[0];
        if (Flags.Contains(verb))
        {
            return args.Count == 1
                ? Result.Success()
                : Result.Failure("[Lab.InvalidArgument] --help and --version take no other arguments");
        }

        if (!SafeSubcommands.TryGetValue(verb, out IReadOnlySet<string>? subcommands))
        {
            return Result.Failure("[Lab.InvalidArgument] verb is not in the CLI allowlist");
        }

        if (args.Count == 1)
        {
            return Result.Success();
        }

        return subcommands.Contains(args[1])
            ? Result.Success()
            : Result.Failure("[Lab.InvalidArgument] subcommand is not in the CLI allowlist");
    }

    private static IReadOnlySet<string> Set(params string[] items) => items.ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// <c>cli_run</c> (Leased): runs the lab CLI inside a Linux container member, with <c>HOME=/home/lab</c>, so the CLI
/// reads the lab's own config and never the workstation's. The host and every non-Linux member are refused (F30).
/// Output goes through the redactor before it returns.
/// </summary>
public sealed class CliRunTool : LabTool<CliRunArgs>
{
    /// <summary>Where the portable CLI build lives in a Linux member.</summary>
    public const string MemberCliPath = "/opt/lab/cli/dhadgar";

    private readonly PoolManager _pool;
    private readonly FaultTargetGuard _guard;
    private readonly IDockerCli _docker;
    private readonly Redactor _redactor;

    /// <summary>
    /// Creates the tool over the pool, the target guard, the Docker client and the redactor.
    /// </summary>
    public CliRunTool(PoolManager pool, FaultTargetGuard guard, IDockerCli docker, Redactor redactor)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    /// <inheritdoc />
    public override string Name => "cli_run";

    /// <inheritdoc />
    public override string Description =>
        "Run the lab CLI inside a Linux container member with its own HOME. Only allowlisted verbs and safe subcommands; the host and Windows members are refused.";

    /// <summary>
    /// Decides whether a member may run the CLI. Pure, so the host refusal is testable without Docker.
    /// </summary>
    /// <param name="member">The member argument as the caller wrote it.</param>
    /// <param name="found">The pool member with that id, or null when there is none.</param>
    public static Result CheckMember(string? member, PoolMember? found)
    {
        if (string.IsNullOrWhiteSpace(member))
        {
            return Result.Failure("[Lab.InvalidArgument] member is required");
        }

        string id = MemberIds.FromArgument(member);
        if (id.Contains(':', StringComparison.Ordinal) || string.Equals(id, "host", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("[Lab.Refused] cli_run never runs on the host");
        }

        if (found is null)
        {
            return Result.Failure("[Lab.NotFound] no pool member has that id");
        }

        if (!string.Equals(found.Dimensions.Os, "linux", StringComparison.Ordinal))
        {
            return Result.Failure("[Lab.Refused] cli_run runs only in a Linux container member");
        }

        return string.IsNullOrWhiteSpace(found.ContainerName)
            ? Result.Failure("[Lab.Refused] the member has no container to run the CLI in")
            : Result.Success();
    }

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(CliRunArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);

        string memberId = MemberIds.FromArgument(args.Member ?? string.Empty);
        PoolListing listing = await _pool.ListAsync(ct).ConfigureAwait(false);
        PoolMember? found = listing.Members.FirstOrDefault(m => string.Equals(m.MemberId, memberId, StringComparison.Ordinal));

        Result memberCheck = CheckMember(args.Member, found);
        if (memberCheck.IsFailure)
        {
            return Result<object>.Failure(memberCheck.Error);
        }

        Result shape = CliAllowlist.Check(args.Args);
        if (shape.IsFailure)
        {
            return Result<object>.Failure(shape.Error);
        }

        Result<ResolvedTarget> resolved = await _guard.ResolveAsync(found!.TargetId, "cli", ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<object>.Failure(resolved.Error);
        }

        // Each argument is its own list item; nothing is joined into a command string.
        var dockerArgs = new List<string> { "exec", "-e", "HOME=/home/lab", found.ContainerName!, MemberCliPath };
        dockerArgs.AddRange(args.Args);

        var clock = Stopwatch.StartNew();
        Result<DockerOutput> output = await _docker.RunAsync(DockerEngine.Linux, dockerArgs, ct).ConfigureAwait(false);
        clock.Stop();
        if (output.IsFailure)
        {
            return Result<object>.Failure(output.Error);
        }

        DockerOutput run = output.Value;
        return Result<object>.Success(new CliRunResult(
            run.ExitCode,
            _redactor.Redact(run.StdOut),
            _redactor.Redact(run.StdErr),
            clock.ElapsedMilliseconds));
    }
}
