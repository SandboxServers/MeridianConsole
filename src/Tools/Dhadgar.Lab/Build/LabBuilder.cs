using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Build;

/// <summary>
/// One project the lab can build (L09 set: <c>gateway</c>, <c>nodes</c>, <c>probe</c>).
/// </summary>
/// <param name="Name">Lab project name used by tools.</param>
/// <param name="Csproj">Path relative to the checkout root, with forward slashes.</param>
/// <param name="DllName">Assembly file name the image runs.</param>
public sealed record BuildProject(string Name, string Csproj, string DllName);

/// <summary>
/// Argument construction and checkout validation for <c>lab_build</c> (L09). Every process runs through
/// <c>ArgumentList</c>; nothing here concatenates a command string. Lock files are never regenerated: restore uses
/// <c>--locked-mode</c> and publish uses <c>--no-restore</c>, and no runtime identifier is ever passed.
/// </summary>
public static class LabBuilder
{
    /// <summary>The projects this packet builds. Other projects arrive in later packets.</summary>
    public static IReadOnlyList<BuildProject> Projects { get; } =
    [
        new("gateway", "src/Dhadgar.Gateway/Dhadgar.Gateway.csproj", "Dhadgar.Gateway.dll"),
        new("nodes", "src/Dhadgar.Nodes/Dhadgar.Nodes.csproj", "Dhadgar.Nodes.dll"),
        new("probe", "src/Tools/Dhadgar.Lab.Probe/Dhadgar.Lab.Probe.csproj", "Dhadgar.Lab.Probe.dll"),
    ];

    /// <summary>Resolves a project name. Unknown names fail with <c>[Lab.InvalidArgument]</c>.</summary>
    public static Result<BuildProject> ResolveProject(string name)
    {
        foreach (BuildProject project in Projects)
        {
            if (string.Equals(project.Name, name, StringComparison.Ordinal))
            {
                return Result<BuildProject>.Success(project);
            }
        }
        return Result<BuildProject>.Failure("[Lab.InvalidArgument] unknown build project");
    }

    /// <summary>
    /// Validates a checkout. It must be the repo root or sit under <c>&lt;repo&gt;\.claude\worktrees\</c>, and no
    /// <c>..</c> segment is allowed. Fails with <c>[Lab.InvalidArgument]</c> otherwise.
    /// </summary>
    public static Result<string> ValidateCheckout(string checkout, string repoRoot, IPathValidator paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(checkout) || string.IsNullOrWhiteSpace(repoRoot))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] checkout and repo root are required");
        }
        if (checkout.Split(['\\', '/']).Contains(".."))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] checkout must not contain '..'");
        }

        string root = Path.GetFullPath(repoRoot);
        string worktrees = Path.Combine(root, ".claude", "worktrees");
        Result<string> validated = paths.ValidatePath(checkout, [root, worktrees]);
        if (validated.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] checkout is outside the repo or its worktrees");
        }
        return Result<string>.Success(validated.Value);
    }

    /// <summary>Build id: first 8 characters of the commit, plus <c>-dirty-&lt;yyyyMMddHHmmss&gt;</c> for a dirty tree.</summary>
    public static string BuildId(string commit, bool dirty, DateTimeOffset at)
    {
        string build8 = commit.Length >= 8 ? commit[..8] : commit;
        return dirty ? $"{build8}-dirty-{at.UtcDateTime:yyyyMMddHHmmss}" : build8;
    }

    /// <summary>Restore arguments: <c>restore &lt;csproj&gt; --locked-mode</c>. Never <c>-r</c>.</summary>
    public static IReadOnlyList<string> RestoreArgs(string csprojPath) =>
        ["restore", csprojPath, "--locked-mode"];

    /// <summary>Publish arguments: <c>publish &lt;csproj&gt; -c Release --no-restore -o &lt;out&gt;</c>. Never <c>-r</c>.</summary>
    public static IReadOnlyList<string> PublishArgs(string csprojPath, string outputDirectory) =>
        ["publish", csprojPath, "-c", "Release", "--no-restore", "-o", outputDirectory];

    /// <summary>Docker build arguments for one project image, with the <c>meridian-lab</c> label.</summary>
    public static IReadOnlyList<string> DockerBuildArgs(string imageTag, string dllName, string contextDirectory, string dockerfile) =>
        ["build", "--build-arg", $"PROJECT_DLL={dllName}", "--label", "meridian-lab=1", "-f", dockerfile, "-t", imageTag, contextDirectory];

    /// <summary>Image tag for a project at a build: <c>meridian-lab/&lt;project&gt;:&lt;build_id&gt;</c>.</summary>
    public static string ImageTag(string project, string buildId) => $"meridian-lab/{project}:{buildId}";

    /// <summary>Lock-file drift marker returned when restore would change a lock file.</summary>
    public const string LockDriftError = "[Lab.ProcessFailed] lock file drift";

    /// <summary>Maps restore output to the drift error when NuGet reports a locked-mode failure (NU1004).</summary>
    public static bool IsLockDrift(string restoreOutput) =>
        restoreOutput.Contains("NU1004", StringComparison.Ordinal) || restoreOutput.Contains("locked mode", StringComparison.OrdinalIgnoreCase);
}
