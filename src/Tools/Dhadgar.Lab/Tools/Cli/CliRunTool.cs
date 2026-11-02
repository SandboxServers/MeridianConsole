using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Stack;
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
/// Options are allowlisted per subcommand (<c>--x=v</c>, <c>--x:v</c>, <c>-abc</c> and <c>--</c> are refused), <c>@</c>
/// response-file arguments are refused anywhere, and <c>auth login</c> is pinned to the lab's development client (D-ML162).
/// Refusal messages never echo an argument's text.
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

    /// <summary>
    /// The options each subcommand accepts (option names from <c>src/Dhadgar.Cli/Program.cs</c>). <c>--identity-url</c> is
    /// deliberately absent: the member config points the CLI at the Gateway (D-ML163).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Shape> Shapes = new Dictionary<string, Shape>(StringComparer.Ordinal)
    {
        ["auth login"] = new(Set("--client-id", "--client-secret"), Set(), 0),
        ["auth status"] = new(Set(), Set(), 0),
        ["auth logout"] = new(Set(), Set(), 0),
        ["nodes list"] = new(Set("--org", "--status", "--skip", "--take"), Set(), 0),
        ["nodes get"] = new(Set("--org"), Set(), 1),
        ["nodes update"] = new(Set("--display-name", "--org"), Set(), 1),
        ["nodes decommission"] = new(Set("--org"), Set("--force"), 1),
        ["nodes maintenance"] = new(Set("--org"), Set(), 1, Set("start", "stop")),
        ["notifications logs"] = new(Set("--limit", "--status", "--org"), Set(), 0),
        ["notifications test"] = new(Set("--title", "--message", "--severity", "--org"), Set(), 0),
        ["discord status"] = new(Set(), Set(), 0),
        ["discord channels"] = new(Set("--guild"), Set(), 0),
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

        if (!subcommands.Contains(args[1]))
        {
            return Result.Failure("[Lab.InvalidArgument] subcommand is not in the CLI allowlist");
        }

        return CheckShape(verb + " " + args[1], args);
    }

    /// <summary>
    /// Walks the tokens after the subcommand against its shape. The first failing rule wins; no message echoes a token.
    /// </summary>
    private static Result CheckShape(string key, IReadOnlyList<string> args)
    {
        if (!Shapes.TryGetValue(key, out Shape? shape))
        {
            return Result.Failure("[Lab.InvalidArgument] subcommand is not in the CLI allowlist");
        }

        int index = 2;
        if (shape.Actions is not null)
        {
            if (args.Count < 3 || !shape.Actions.Contains(args[2]))
            {
                return Result.Failure("[Lab.InvalidArgument] subcommand needs start or stop");
            }

            index = 3;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? clientId = null;
        string? clientSecret = null;
        int positionals = 0;
        for (int i = index; i < args.Count; i++)
        {
            string token = args[i];
            if (token.StartsWith('@'))
            {
                return Result.Failure("[Lab.InvalidArgument] '@' arguments (response files) are refused");
            }

            if (token.StartsWith('-'))
            {
                if (shape.ValueOptions.Contains(token))
                {
                    if (!seen.Add(token))
                    {
                        return Result.Failure("[Lab.InvalidArgument] option given twice");
                    }

                    if (i + 1 >= args.Count || args[i + 1].StartsWith('-') || args[i + 1].StartsWith('@'))
                    {
                        return Result.Failure("[Lab.InvalidArgument] option needs a plain value");
                    }

                    string value = args[i + 1];
                    if (token == "--client-id")
                    {
                        clientId = value;
                    }
                    else if (token == "--client-secret")
                    {
                        clientSecret = value;
                    }

                    i++;
                    continue;
                }

                if (!shape.FlagOptions.Contains(token))
                {
                    return Result.Failure("[Lab.InvalidArgument] option is not allowed for this subcommand");
                }

                if (!seen.Add(token))
                {
                    return Result.Failure("[Lab.InvalidArgument] option given twice");
                }

                continue;
            }

            positionals++;
            if (positionals > shape.MaxPositionals)
            {
                return Result.Failure("[Lab.InvalidArgument] too many positional arguments");
            }
        }

        if (key == "auth login"
            && !(string.Equals(clientId, IdentityLabAdapter.DevClientId, StringComparison.Ordinal)
                 && string.Equals(clientSecret, IdentityLabAdapter.DevClientSecret, StringComparison.Ordinal)))
        {
            return Result.Failure("[Lab.InvalidArgument] auth login takes only the lab's development client");
        }

        return Result.Success();
    }

    private static IReadOnlySet<string> Set(params string[] items) => items.ToHashSet(StringComparer.Ordinal);

    /// <summary>The options and positional count one subcommand accepts.</summary>
    private sealed record Shape(IReadOnlySet<string> ValueOptions, IReadOnlySet<string> FlagOptions, int MaxPositionals, IReadOnlySet<string>? Actions = null);
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

    /// <summary>
    /// Installs the CLI and its config into one pool member when it is a Linux container with a latest build that has a CLI.
    /// Used by <c>pool_acquire</c>, <c>member_reset</c> and <c>uat_run</c> (through <c>CliMemberPreparer</c>), because a reset recreates the container from the bare image.
    /// Returns success without doing anything for other members or when there is no CLI to install.
    /// </summary>
    public static async Task<Result> InstallIntoMemberAsync(IDockerCli docker, LabPaths paths, IPathValidator validator, PoolMember member, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(member);
        if (member.Dimensions.Os != "linux" || string.IsNullOrWhiteSpace(member.ContainerName))
        {
            return Result.Success();
        }

        string? cliDirectory = LatestCliDirectory(paths, validator);
        if (cliDirectory is null)
        {
            return Result.Success();
        }

        Result installed = await InstallAsync(docker, member.ContainerName!, cliDirectory, ct).ConfigureAwait(false);
        if (installed.IsFailure)
        {
            return installed;
        }

        // The admin key is written per call by cli_run, because the vault has none until stack_up.
        return await InstallConfigAsync(docker, member.ContainerName!, StagingDirectory(paths), null, validator, ct).ConfigureAwait(false);
    }

    /// <summary>The directory in a Linux member that holds the CLI's config (<c>HOME=/home/lab</c>).</summary>
    public const string MemberConfigDirectory = "/home/lab/.dhadgar";

    /// <summary>The CLI config file in a Linux member.</summary>
    public const string MemberConfigPath = "/home/lab/.dhadgar/config.json";

    /// <summary>The Gateway the member's CLI calls: the lab network name, not a host port.</summary>
    public const string MemberGatewayUrl = "http://meridian-lab-gateway:8080";

    /// <summary>The Notifications service the CLI calls direct for admin commands (the lab network name, as in ProxyCatalog).</summary>
    public const string MemberNotificationsUrl = "http://meridian-lab-notifications:8080";

    /// <summary>The Identity URL the CLI posts to: the Gateway's <c>/api/v1/identity</c> route (D-ML163 a).</summary>
    public const string MemberIdentityUrl = "http://meridian-lab-gateway:8080/api/v1/identity";

    /// <summary>The Nodes base URL the CLI calls: the Gateway's <c>/api/v1</c> route (D-ML163 a).</summary>
    public const string MemberNodesUrl = "http://meridian-lab-gateway:8080/api/v1";

    /// <summary>The Discord service the CLI calls direct for admin commands (the lab network name).</summary>
    public const string MemberDiscordUrl = "http://meridian-lab-discord:8080";

    /// <summary>
    /// The member's <c>config.json</c> text: the five service URLs (<c>gateway_url</c>, <c>identity_url</c>, <c>nodes_url</c>,
    /// <c>notifications_url</c>, <c>discord_url</c>) and <c>admin_api_key</c> when one is given (the CLI's <c>CliConfig</c>
    /// reads them all). Client credentials are still never written (D-ML162 puts them on argv only): <c>auth login</c>
    /// takes its secret only from <c>--client-secret</c> or a prompt, never from this file.
    /// </summary>
    public static string RenderMemberConfig(string? adminApiKey)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gateway_url"] = MemberGatewayUrl,
            ["identity_url"] = MemberIdentityUrl,
            ["nodes_url"] = MemberNodesUrl,
            ["notifications_url"] = MemberNotificationsUrl,
            ["discord_url"] = MemberDiscordUrl,
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

    /// <summary>The vault's admin key a verb needs in the member config, or null when the verb needs none (D-ML162, ruling N2).</summary>
    /// <param name="verb">The CLI verb (the first argument).</param>
    public static string? AdminCredentialFor(string verb) => verb switch
    {
        "notifications" => LabCredentialRefs.NotificationsAdmin,
        "discord" => LabCredentialRefs.DiscordAdmin,
        _ => null,
    };

    /// <summary>The dedicated folder under the lab state root where a member config is staged before <c>docker cp</c>.</summary>
    /// <param name="paths">The lab's paths.</param>
    public static string StagingDirectory(LabPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.State, "cli-config");
    }

    /// <summary>
    /// Deletes stale staged configs: <c>cli-config-*.json</c> files in <paramref name="directory"/> whose last write is older
    /// than <paramref name="now"/> minus <paramref name="maxAge"/>. This covers a process killed between write and delete.
    /// Returns the count removed; returns 0 when the directory does not exist. Per-file IO failures are skipped.
    /// </summary>
    internal static int SweepStale(string directory, DateTimeOffset now, TimeSpan maxAge)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        DateTime cutoff = now.Subtract(maxAge).UtcDateTime;
        int removed = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "cli-config-*.json"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (IOException)
                {
                    // The file is locked or already gone; a later sweep retries it.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same: the file could not be removed by this process.
                }
            }
        }
        catch (IOException)
        {
            return removed;
        }
        catch (UnauthorizedAccessException)
        {
            return removed;
        }

        return removed;
    }

    /// <summary>
    /// Writes <c>/home/lab/.dhadgar/config.json</c> into one Linux member. The JSON is staged in a new file under
    /// <paramref name="stagingDirectory"/>, copied with <c>docker cp</c>, and deleted in <c>finally</c>. The staging folder
    /// is validated under its parent, and stale staged files are swept first. The key never goes on a command line or into
    /// a log. IO failures return <c>[Lab.Io]</c>; Docker non-zero exits return <c>[Lab.Docker]</c>.
    /// </summary>
    public static async Task<Result> InstallConfigAsync(IDockerCli docker, string container, string stagingDirectory, string? adminApiKey, IPathValidator validator, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(stagingDirectory);
        ArgumentNullException.ThrowIfNull(validator);
        string? parent = Path.GetDirectoryName(Path.GetFullPath(stagingDirectory));
        if (parent is null)
        {
            return Result.Failure("[Lab.Io] the member's CLI config staging folder is not under the lab state root");
        }

        Result<string> checkedDirectory = validator.ValidatePath(stagingDirectory, [parent]);
        if (checkedDirectory.IsFailure)
        {
            return Result.Failure(checkedDirectory.Error);
        }

        string staged = Path.Combine(checkedDirectory.Value!, "cli-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(checkedDirectory.Value!);
            SweepStale(checkedDirectory.Value!, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
            string json = RenderMemberConfig(adminApiKey);
            // The writer is disposed (and the file closed) before docker cp opens the file.
            await using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
            }

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
    private readonly LabCredentialRefs _credentials;
    private readonly SecretVault _vault;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool over the pool, the target guard, the Docker client, the redactor, the admin key references and
    /// vault (admin verbs), the lab paths and the path validator (the staged member config).
    /// </summary>
    public CliRunTool(
        PoolManager pool,
        FaultTargetGuard guard,
        IDockerCli docker,
        Redactor redactor,
        LabCredentialRefs credentials,
        SecretVault vault,
        LabPaths paths,
        IPathValidator validator)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public override string Name => "cli_run";

    /// <inheritdoc />
    public override string Description =>
        "Run the lab CLI inside a Linux container member with its own HOME. Only allowlisted verbs and safe subcommands; the host and Windows members are refused. "
        + "Admin verbs (notifications, discord) write the vault's admin key into the member config for the call and remove it afterwards; that rewrite also clears any stored login.";

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

        Result<ResolvedTarget> resolved = await _guard.ResolveMemberForExecAsync(found!.TargetId, ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<object>.Failure(resolved.Error);
        }

        string container = resolved.Value!.ContainerId ?? string.Empty;
        if (container.Length == 0)
        {
            return Result<object>.Failure("[Lab.BlastRadius] the member's container could not be resolved");
        }

        // An admin verb needs its vault key in the member config for this call only (ruling N2). The key is read here and
        // is never logged or put on a command line; the config is rewritten without it in finally.
        string? credential = CliMemberInstall.AdminCredentialFor(args.Args[0]);
        string? key = null;
        if (credential is not null)
        {
            string? reference = _credentials.Get(credential);
            if (reference is null)
            {
                return Result<object>.Failure("[Lab.NotFound] the stack has no admin keys; run stack_up first");
            }

            Result<string> vaulted = _vault.Use(reference, value => value.ToString());
            if (vaulted.IsFailure)
            {
                return Result<object>.Failure(vaulted.Error);
            }

            key = vaulted.Value;
        }

        string staging = CliMemberInstall.StagingDirectory(_paths);
        Result<object> outcome;
        bool scrubFailed = false;
        try
        {
            outcome = await SeedAndRunAsync(container, args, key, staging, ct).ConfigureAwait(false);
        }
        finally
        {
            if (credential is not null)
            {
                // CancellationToken.None: the scrub must run even when the call was cancelled.
                Result scrubbed = await CliMemberInstall.InstallConfigAsync(_docker, container, staging, null, _validator, CancellationToken.None)
                    .ConfigureAwait(false);
                scrubFailed = scrubbed.IsFailure;
            }
        }

        if (scrubFailed)
        {
            return Result<object>.Failure("[Lab.Docker] could not remove the admin key from the member's CLI config");
        }

        return outcome;
    }

    /// <summary>
    /// Writes the admin key into the member config when one is given, then runs the CLI in the member. Each argument is its
    /// own list item; nothing is joined into a command string.
    /// </summary>
    private async Task<Result<object>> SeedAndRunAsync(string container, CliRunArgs args, string? key, string staging, CancellationToken ct)
    {
        if (key is not null)
        {
            Result seeded = await CliMemberInstall.InstallConfigAsync(_docker, container, staging, key, _validator, ct).ConfigureAwait(false);
            if (seeded.IsFailure)
            {
                return Result<object>.Failure(seeded.Error);
            }
        }

        var dockerArgs = new List<string> { "exec", "-e", "HOME=/home/lab", container, MemberCliPath };
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
