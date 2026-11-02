using System.Diagnostics;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools.Pool;
using Dhadgar.Lab.Uat.Bundle;
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
/// Copies the latest build's CLI (L37, <c>LabBuilder.PublishCliAsync</c>) into a Linux pool member at <c>/opt/lab/cli/</c>.
/// Every <c>docker</c> call is an <c>ArgumentList</c> item list; the host folder comes from the lab's own build root.
/// </summary>
public static class CliMemberInstall
{
    /// <summary>The directory in a Linux member the CLI is copied into.</summary>
    public const string MemberCliDirectory = "/opt/lab/cli";

    /// <summary>
    /// The CLI folder of the lab's latest build, or null when the latest build has no CLI (or there is no build).
    /// Never throws.
    /// </summary>
    public static string? LatestCliDirectory(LabPaths paths, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validator);
        BuildStamp stamp = LastBuildReader.Read(paths, validator);
        if (string.IsNullOrEmpty(stamp.Id) || stamp.Id == BuildStamp.None.Id)
        {
            return null;
        }

        Result<string> dir = validator.ValidatePath(Path.Combine(paths.Builds, stamp.Id, "cli"), [paths.Builds]);
        return dir.IsSuccess && Directory.Exists(dir.Value) ? dir.Value : null;
    }

    /// <summary>The <c>docker exec</c> that creates the member's CLI folder.</summary>
    public static IReadOnlyList<string> MkdirArgs(string container) =>
        ["exec", "-u", "root", container, "mkdir", "-p", MemberCliDirectory];

    /// <summary>The <c>docker cp</c> that copies the CLI folder's contents into the member.</summary>
    public static IReadOnlyList<string> CopyArgs(string container, string hostCliDirectory) =>
        ["cp", Path.Combine(hostCliDirectory, "."), container + ":" + MemberCliDirectory + "/"];

    /// <summary>The <c>docker exec</c> that makes the launcher executable (a Windows copy has no execute bit).</summary>
    public static IReadOnlyList<string> ChmodArgs(string container) =>
        ["exec", "-u", "root", container, "chmod", "755", CliRunTool.MemberCliPath];

    /// <summary>
    /// Installs the CLI into one Linux member: mkdir, copy, chmod. Any non-zero exit fails with <c>[Lab.Docker]</c>.
    /// </summary>
    public static Task<Result> InstallAsync(IDockerCli docker, string container, string hostCliDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        return RunStepsAsync(docker, [MkdirArgs(container), CopyArgs(container, hostCliDirectory), ChmodArgs(container)], ct);
    }

    /// <summary>The directory in a Linux member that holds the CLI's config (<c>HOME=/home/lab</c>).</summary>
    public const string MemberConfigDirectory = "/home/lab/.dhadgar";

    /// <summary>The CLI config file in a Linux member.</summary>
    public const string MemberConfigPath = "/home/lab/.dhadgar/config.json";

    /// <summary>The Gateway the member's CLI calls: the lab network name, not a host port.</summary>
    public const string MemberGatewayUrl = "http://meridian-lab-gateway:8080";

    /// <summary>The Notifications service the CLI calls direct for admin commands (the lab network name, as in ProxyCatalog).</summary>
    public const string MemberNotificationsUrl = "http://meridian-lab-notifications:8080";

    /// <summary>
    /// The member's <c>config.json</c> text: <c>gateway_url</c>, <c>notifications_url</c>, and <c>admin_api_key</c> when
    /// one is given (the CLI's <c>CliConfig</c> reads them all). Client credentials are not written: <c>auth login</c>
    /// takes its secret only from <c>--client-secret</c> or a prompt, never from this file.
    /// </summary>
    public static string RenderMemberConfig(string? adminApiKey)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gateway_url"] = MemberGatewayUrl,
            ["notifications_url"] = MemberNotificationsUrl,
        };
        if (!string.IsNullOrEmpty(adminApiKey))
        {
            values["admin_api_key"] = adminApiKey;
        }

        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The <c>docker</c> steps that write the member's config from a staged file: mkdir, copy, owner as the member
    /// home, mode 600. Each is an <c>ArgumentList</c> item list.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ConfigSteps(string container, string stagedFile) =>
    [
        ["exec", "-u", "root", container, "mkdir", "-p", MemberConfigDirectory],
        ["cp", stagedFile, container + ":" + MemberConfigPath],
        ["exec", "-u", "root", container, "chown", "--reference=/home/lab", MemberConfigDirectory, MemberConfigPath],
        ["exec", "-u", "root", container, "chmod", "600", MemberConfigPath],
    ];

    /// <summary>
    /// Writes <c>/home/lab/.dhadgar/config.json</c> into one Linux member. The JSON is staged in a new file under
    /// <paramref name="stagingDirectory"/>, copied with <c>docker cp</c>, and deleted in <c>finally</c>. The key never
    /// goes on a command line or into a log. IO failures return <c>[Lab.Io]</c>; Docker non-zero exits return <c>[Lab.Docker]</c>.
    /// </summary>
    public static async Task<Result> InstallConfigAsync(IDockerCli docker, string container, string stagingDirectory, string? adminApiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(stagingDirectory);
        string staged = Path.Combine(stagingDirectory, "cli-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await File.WriteAllTextAsync(staged, RenderMemberConfig(adminApiKey), ct).ConfigureAwait(false);
            return await RunStepsAsync(docker, ConfigSteps(container, staged), ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return Result.Failure("[Lab.Io] could not stage the member's CLI config");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] could not stage the member's CLI config");
        }
        finally
        {
            DeleteStaged(staged);
        }
    }

    private static async Task<Result> RunStepsAsync(IDockerCli docker, IReadOnlyList<IReadOnlyList<string>> steps, CancellationToken ct)
    {
        foreach (IReadOnlyList<string> step in steps)
        {
            Result<DockerOutput> output = await docker.RunAsync(DockerEngine.Linux, step, ct).ConfigureAwait(false);
            if (output.IsFailure)
            {
                return Result.Failure(output.Error);
            }

            if (output.Value!.ExitCode != 0)
            {
                return Result.Failure("[Lab.Docker] installing the CLI into the member failed");
            }
        }

        return Result.Success();
    }

    private static void DeleteStaged(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The staged file is gone or locked; nothing else to do.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: the file could not be removed; the caller already has the result.
        }
    }
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
