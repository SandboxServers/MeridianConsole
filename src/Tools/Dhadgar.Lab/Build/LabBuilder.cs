using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Build;

/// <summary>
/// One project the lab can build (L09 set: <c>gateway</c>, <c>nodes</c>, <c>probe</c>).
/// </summary>
/// <param name="Name">Lab project name used by tools.</param>
/// <param name="Csproj">Path relative to the checkout root, with forward slashes.</param>
/// <param name="DllName">Assembly file name the image runs.</param>
/// <param name="NodeDirectory">For a Node app: its folder relative to the checkout root (<c>null</c> for .NET projects).</param>
/// <param name="NodeEntry">For a Node app: the entry the image runs, relative to the app folder.</param>
public sealed record BuildProject(string Name, string Csproj, string DllName, string? NodeDirectory = null, string? NodeEntry = null)
{
    /// <summary>True for a Node app built from <c>tools/lab/images/node-app.Dockerfile</c> instead of a .NET publish.</summary>
    public bool IsNode => NodeDirectory is not null;
}

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
        new("notifications", "src/Dhadgar.Notifications/Dhadgar.Notifications.csproj", "Dhadgar.Notifications.dll"),
        new("discord", "src/Dhadgar.Discord/Dhadgar.Discord.csproj", "Dhadgar.Discord.dll"),
        new("probe", "src/Tools/Dhadgar.Lab.Probe/Dhadgar.Lab.Probe.csproj", "meridian-lab-probe.dll"),
        new("identity", "src/Dhadgar.Identity/Dhadgar.Identity.csproj", "Dhadgar.Identity.dll"),
        new("betterauth", "src/Dhadgar.BetterAuth/package.json", string.Empty, "src/Dhadgar.BetterAuth", "src/server.js"),
        new("panel", "src/Dhadgar.Panel/package.json", string.Empty, "src/Dhadgar.Panel", "dist/server/entry.mjs"),
    ];

    /// <summary>Folder names never staged into a Node build context (installed, generated or .NET shim output).</summary>
    private static readonly string[] NodeExcludedDirectories = ["node_modules", "dist", ".git", ".astro", "coverage", "bin", "obj"];

    /// <summary>File names never staged: package-manager credentials and local secret files (security N4).</summary>
    private static readonly string[] NodeExcludedFileNames = [".npmrc", ".yarnrc", ".yarnrc.yml", ".dev.vars", ".secrets", "secrets.json"];

    /// <summary>Key and certificate extensions never staged.</summary>
    private static readonly string[] NodeExcludedExtensions = [".pem", ".key", ".pfx", ".p12"];

    /// <summary>The lab-only Secrets stub (D-ML146), relative to the checkout, and where it lands in the stage.</summary>
    private const string SecretsStubSource = "tools/lab/images/lab-secrets-stub.mjs";

    /// <summary>True when a file name may be staged: not an env file, not a credential file, not a key or certificate.</summary>
    internal static bool IsStagedFile(string name) =>
        !name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
        && !NodeExcludedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
        && !NodeExcludedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

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

    /// <summary>The lab CLI project (L37), relative to the checkout root.</summary>
    public const string CliCsproj = "src/Dhadgar.Cli/Dhadgar.Cli.csproj";

    /// <summary>
    /// Name of the Linux launcher written beside the CLI publish. The CLI's apphost is built for the build host (a Windows
    /// <c>.exe</c> here), so the member runs <c>dhadgar</c>, a shell launcher for the portable <c>dhadgar.dll</c>.
    /// </summary>
    public const string CliLauncherName = "dhadgar";

    /// <summary>The launcher's text: it runs the portable CLI dll under the member's <c>dotnet</c> runtime.</summary>
    public const string CliLauncherText = "#!/bin/sh\nexec dotnet /opt/lab/cli/dhadgar.dll \"$@\"\n";

    /// <summary>
    /// CLI publish arguments: a portable publish with no runtime identifier. <c>SkipGlobalToolInstall=true</c> stops the
    /// project's AfterBuild target from installing a global tool on the workstation during the publish.
    /// </summary>
    public static IReadOnlyList<string> CliPublishArgs(string csprojPath, string outputDirectory) =>
        ["publish", csprojPath, "-c", "Release", "--no-restore", "-p:SkipGlobalToolInstall=true", "-o", outputDirectory];

    /// <summary>
    /// Docker build arguments for one project image, with the <c>meridian-lab</c> label. A mutant build passes its
    /// <c>meridian-lab-mutant=&lt;id&gt;</c> label as <paramref name="extraLabel"/> (L54, A2).
    /// </summary>
    public static IReadOnlyList<string> DockerBuildArgs(string imageTag, string dllName, string contextDirectory, string dockerfile, string? extraLabel = null)
    {
        var args = new List<string> { "build", "--build-arg", $"PROJECT_DLL={dllName}", "--label", "meridian-lab=1" };
        if (extraLabel is not null)
        {
            args.Add("--label");
            args.Add(extraLabel);
        }

        args.AddRange(["-f", dockerfile, "-t", imageTag, contextDirectory]);
        return args;
    }

    /// <summary>
    /// Docker build arguments for a Node app image from <c>node-app.Dockerfile</c>: <c>APP_DIR</c> is the staged app folder
    /// and <c>APP_ENTRY</c> the entry it runs (L34).
    /// </summary>
    public static IReadOnlyList<string> NodeDockerBuildArgs(string imageTag, string entry, string contextDirectory, string dockerfile) =>
        ["build", "--build-arg", "APP_DIR=app", "--build-arg", $"APP_ENTRY={entry}", "--label", "meridian-lab=1", "-f", dockerfile, "-t", imageTag, contextDirectory];

    /// <summary>Image tag for a project at a build: <c>meridian-lab/&lt;project&gt;:&lt;build_id&gt;</c>.</summary>
    public static string ImageTag(string project, string buildId) => $"meridian-lab/{project}:{buildId}";

    /// <summary>The tag <c>lab_build</c> gives the newest Linux member image: <c>meridian-lab/linux-member:latest</c>.</summary>
    public const string LinuxMemberLatestTag = "meridian-lab/linux-member:latest";

    /// <summary>Docker arguments that tag a built Linux member image as the latest one the pool uses.</summary>
    /// <param name="memberTag">The build-id tag from <see cref="ImageTag"/>.</param>
    public static IReadOnlyList<string> LinuxMemberLatestTagArgs(string memberTag) => ["tag", memberTag, LinuxMemberLatestTag];

    /// <summary>
    /// The Dockerfile under <c>tools/lab/images</c> for a project: the probe image (sink, L36) uses <c>probe.Dockerfile</c>,
    /// every other service uses <c>dotnet-service.Dockerfile</c>.
    /// </summary>
    internal static string DockerfileFor(string projectName) =>
        string.Equals(projectName, "probe", StringComparison.Ordinal) ? "probe.Dockerfile" : "dotnet-service.Dockerfile";

    /// <summary>Lock-file drift marker returned when restore would change a lock file.</summary>
    public const string LockDriftError = "[Lab.ProcessFailed] lock file drift";

    /// <summary>Maps restore output to the drift error when NuGet reports a locked-mode failure (NU1004).</summary>
    public static bool IsLockDrift(string restoreOutput) =>
        restoreOutput.Contains("NU1004", StringComparison.Ordinal) || restoreOutput.Contains("locked mode", StringComparison.OrdinalIgnoreCase);

    /// <summary>Upper bound for one git, dotnet or docker child (R-TIME).</summary>
    public static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Builds the projects from a checkout: <c>git rev-parse</c> and <c>status</c>, then <c>dotnet restore --locked-mode</c>
    /// and <c>dotnet publish --no-restore</c> into <c>&lt;Builds&gt;\&lt;build_id&gt;\&lt;project&gt;</c>, then <c>docker build</c>.
    /// Writes <c>build.json</c> and <c>&lt;Builds&gt;\latest.txt</c>. Only output under <see cref="LabPaths.Builds"/> is written.
    /// </summary>
    public static async Task<Result<LabBuildDto>> BuildAsync(
        string checkout,
        IReadOnlyList<BuildProject> projects,
        IDockerCli docker,
        LabPaths paths,
        IPathValidator validator,
        TimeProvider time,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(time);

        Result<string> root = ValidateCheckout(checkout, paths.RepoRoot, validator);
        if (root.IsFailure)
        {
            return Result<LabBuildDto>.Failure(root.Error);
        }

        string checkoutPath = root.Value!;
        Result<string> commitOut = await RunAsync("git", ["-C", checkoutPath, "rev-parse", "HEAD"], checkoutPath, ct).ConfigureAwait(false);
        Result<string> statusOut = await RunAsync("git", ["-C", checkoutPath, "status", "--porcelain"], checkoutPath, ct).ConfigureAwait(false);
        if (commitOut.IsFailure || statusOut.IsFailure)
        {
            return Result<LabBuildDto>.Failure(commitOut.IsFailure ? commitOut.Error : statusOut.Error);
        }

        string commit = commitOut.Value!.Trim();
        if (commit.Length != 40 || !commit.All(char.IsAsciiHexDigitLower))
        {
            return Result<LabBuildDto>.Failure("[Lab.ProcessFailed] checkout has no commit");
        }

        bool dirty = statusOut.Value!.Trim().Length > 0;
        DateTimeOffset at = time.GetUtcNow();
        string buildId = BuildId(commit, dirty, at);
        Result<string> outputRoot = validator.ValidatePath(Path.Combine(paths.Builds, buildId), [paths.Builds]);
        if (outputRoot.IsFailure)
        {
            return Result<LabBuildDto>.Failure(outputRoot.Error);
        }

        var images = new List<string>();
        foreach (BuildProject project in projects)
        {
            if (project.IsNode)
            {
                Result<string> nodeImage = await BuildNodeAppAsync(project, checkoutPath, outputRoot.Value!, buildId, docker, paths, validator, ct).ConfigureAwait(false);
                if (nodeImage.IsFailure)
                {
                    return Result<LabBuildDto>.Failure(nodeImage.Error);
                }

                images.Add(nodeImage.Value!);
                continue;
            }

            Result<string> csproj = validator.ValidatePath(Path.Combine(checkoutPath, project.Csproj.Replace('/', Path.DirectorySeparatorChar)), [checkoutPath]);
            Result<string> publishDir = validator.ValidatePath(Path.Combine(outputRoot.Value!, project.Name), [paths.Builds]);
            Result<string> dockerfile = validator.ValidatePath(Path.Combine(checkoutPath, "tools", "lab", "images", DockerfileFor(project.Name)), [checkoutPath]);
            if (csproj.IsFailure || publishDir.IsFailure || dockerfile.IsFailure)
            {
                return Result<LabBuildDto>.Failure("[Lab.InvalidArgument] build path is not valid");
            }

            Result<string> restore = await RunAsync("dotnet", RestoreArgs(csproj.Value!), checkoutPath, ct).ConfigureAwait(false);
            if (restore.IsFailure)
            {
                return Result<LabBuildDto>.Failure(restore.Error);
            }

            Result<string> publish = await RunAsync("dotnet", PublishArgs(csproj.Value!, publishDir.Value!), checkoutPath, ct).ConfigureAwait(false);
            if (publish.IsFailure)
            {
                return Result<LabBuildDto>.Failure(publish.Error);
            }

            string tag = ImageTag(project.Name, buildId);
            Result<DockerOutput> image = await docker.RunAsync(
                DockerEngine.Linux,
                DockerBuildArgs(tag, project.DllName, publishDir.Value!, dockerfile.Value!),
                ct).ConfigureAwait(false);
            if (image.IsFailure || image.Value!.ExitCode != 0)
            {
                return Result<LabBuildDto>.Failure("[Lab.Docker] docker build failed");
            }

            images.Add(tag);

            // The probe also ships as the Linux pool member image (L30); the member image carries no service.
            if (project.Name == "probe")
            {
                Result<string> memberDockerfile = validator.ValidatePath(Path.Combine(checkoutPath, "tools", "lab", "images", "linux-member.Dockerfile"), [checkoutPath]);
                if (memberDockerfile.IsFailure)
                {
                    return Result<LabBuildDto>.Failure("[Lab.InvalidArgument] build path is not valid");
                }

                string memberTag = ImageTag("linux-member", buildId);
                Result<DockerOutput> member = await docker.RunAsync(
                    DockerEngine.Linux,
                    DockerBuildArgs(memberTag, project.DllName, publishDir.Value!, memberDockerfile.Value!),
                    ct).ConfigureAwait(false);
                if (member.IsFailure || member.Value!.ExitCode != 0)
                {
                    return Result<LabBuildDto>.Failure("[Lab.Docker] docker build failed");
                }

                images.Add(memberTag);

                // The pool's default image is the latest member image (LinuxContainerProvider.DefaultImage).
                Result<DockerOutput> latest = await docker.RunAsync(
                    DockerEngine.Linux,
                    LinuxMemberLatestTagArgs(memberTag),
                    ct).ConfigureAwait(false);
                if (latest.IsFailure || latest.Value!.ExitCode != 0)
                {
                    return Result<LabBuildDto>.Failure("[Lab.Docker] docker tag failed");
                }
            }
        }

        Result cli = await PublishCliAsync(outputRoot.Value!, checkoutPath, paths, validator, ct).ConfigureAwait(false);
        if (cli.IsFailure)
        {
            return Result<LabBuildDto>.Failure(cli.Error);
        }

        var projectNames = projects.Select(project => project.Name).ToList();
        var dto = new LabBuildDto(buildId, commit, dirty, checkoutPath, projectNames, images, at);
        Result written = WriteOutputs(paths, validator, outputRoot.Value!, dto);
        return written.IsFailure ? Result<LabBuildDto>.Failure(written.Error) : Result<LabBuildDto>.Success(dto);
    }

    /// <summary>
    /// Publishes the lab CLI (L37) portable into <c>&lt;outputRoot&gt;\cli</c> with the Linux launcher beside it. Restore
    /// runs first with <c>--locked-mode</c>, like every other project. Only paths under <see cref="LabPaths.Builds"/> are written.
    /// </summary>
    internal static async Task<Result> PublishCliAsync(string outputRoot, string checkoutPath, LabPaths paths, IPathValidator validator, CancellationToken ct)
    {
        Result<string> csproj = validator.ValidatePath(Path.Combine(checkoutPath, CliCsproj.Replace('/', Path.DirectorySeparatorChar)), [checkoutPath]);
        Result<string> cliDir = validator.ValidatePath(Path.Combine(outputRoot, "cli"), [paths.Builds]);
        if (csproj.IsFailure || cliDir.IsFailure)
        {
            return Result.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        Result<string> restore = await RunAsync("dotnet", RestoreArgs(csproj.Value!), checkoutPath, ct).ConfigureAwait(false);
        if (restore.IsFailure)
        {
            return Result.Failure(restore.Error);
        }

        Result<string> publish = await RunAsync("dotnet", CliPublishArgs(csproj.Value!, cliDir.Value!), checkoutPath, ct).ConfigureAwait(false);
        if (publish.IsFailure)
        {
            return Result.Failure(publish.Error);
        }

        try
        {
            File.WriteAllText(Path.Combine(cliDir.Value!, CliLauncherName), CliLauncherText);
        }
        catch (IOException)
        {
            return Result.Failure("[Lab.Io] could not write the CLI launcher");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] could not write the CLI launcher");
        }

        return Result.Success();
    }

    /// <summary>
    /// Stages a Node app under <c>&lt;outputRoot&gt;\&lt;project&gt;</c>: <c>app/</c> (the app), <c>SharedAuth/</c> and
    /// <c>lab/secrets-stub.mjs</c> (D-ML146). Env files, credential files, keys, installed or generated folders and reparse
    /// points are never copied. Returns the stage folder.
    /// </summary>
    internal static Result<string> StageNodeApp(BuildProject project, string checkoutPath, string outputRoot, LabPaths paths, IPathValidator validator)
    {
        Result<string> appSource = validator.ValidatePath(Path.Combine(checkoutPath, project.NodeDirectory!.Replace('/', Path.DirectorySeparatorChar)), [checkoutPath]);
        Result<string> sharedSource = validator.ValidatePath(Path.Combine(checkoutPath, "src", "Dhadgar.SharedAuth"), [checkoutPath]);
        Result<string> stage = validator.ValidatePath(Path.Combine(outputRoot, project.Name), [paths.Builds]);
        Result<string> stubSource = validator.ValidatePath(Path.Combine(checkoutPath, SecretsStubSource.Replace('/', Path.DirectorySeparatorChar)), [checkoutPath]);
        if (appSource.IsFailure || sharedSource.IsFailure || stage.IsFailure || stubSource.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        Result<string> appDest = validator.ValidatePath(Path.Combine(stage.Value!, "app"), [paths.Builds]);
        Result<string> sharedDest = validator.ValidatePath(Path.Combine(stage.Value!, "SharedAuth"), [paths.Builds]);
        Result<string> stubDest = validator.ValidatePath(Path.Combine(stage.Value!, "lab", "secrets-stub.mjs"), [paths.Builds]);
        if (appDest.IsFailure || sharedDest.IsFailure || stubDest.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        Result copiedApp = CopyTree(appSource.Value!, appDest.Value!);
        if (copiedApp.IsFailure)
        {
            return Result<string>.Failure(copiedApp.Error);
        }

        Result copiedShared = CopyTree(sharedSource.Value!, sharedDest.Value!);
        if (copiedShared.IsFailure)
        {
            return Result<string>.Failure(copiedShared.Error);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stubDest.Value!)!);
            File.Copy(stubSource.Value!, stubDest.Value!, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.Io] build source could not be staged");
        }

        return Result<string>.Success(stage.Value!);
    }

    /// <summary>
    /// Stages a Node app (<see cref="StageNodeApp"/>), then <c>docker build</c>s it from <c>node-app.Dockerfile</c>.
    /// Only source is copied; <c>npm ci</c> runs inside the image.
    /// </summary>
    private static async Task<Result<string>> BuildNodeAppAsync(
        BuildProject project,
        string checkoutPath,
        string outputRoot,
        string buildId,
        IDockerCli docker,
        LabPaths paths,
        IPathValidator validator,
        CancellationToken ct)
    {
        Result<string> dockerfile = validator.ValidatePath(Path.Combine(checkoutPath, "tools", "lab", "images", "node-app.Dockerfile"), [checkoutPath]);
        if (dockerfile.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        Result<string> stage = StageNodeApp(project, checkoutPath, outputRoot, paths, validator);
        if (stage.IsFailure)
        {
            return stage;
        }

        string tag = ImageTag(project.Name, buildId);
        Result<DockerOutput> image = await docker.RunAsync(
            DockerEngine.Linux,
            NodeDockerBuildArgs(tag, project.NodeEntry!, stage.Value!, dockerfile.Value!),
            ct).ConfigureAwait(false);
        if (image.IsFailure || image.Value!.ExitCode != 0)
        {
            return Result<string>.Failure("[Lab.Docker] docker build failed");
        }

        return Result<string>.Success(tag);
    }

    /// <summary>
    /// Copies a source tree without installed or generated folders (<see cref="NodeExcludedDirectories"/>), without env,
    /// credential, key and certificate files (<see cref="IsStagedFile"/>, <see cref="NodeExcludedFileNames"/>,
    /// <see cref="NodeExcludedExtensions"/>) and without reparse points. Fails with <c>[Lab.Io]</c> when a file cannot be copied.
    /// </summary>
    internal static Result CopyTree(string source, string destination)
    {
        try
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                if (IsStagedFile(name) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                {
                    File.Copy(file, Path.Combine(destination, name), overwrite: true);
                }
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (NodeExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
                    || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                Result nested = CopyTree(directory, Path.Combine(destination, name));
                if (nested.IsFailure)
                {
                    return nested;
                }
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] build source could not be staged");
        }
    }

    private static Result WriteOutputs(LabPaths paths, IPathValidator validator, string outputRoot, LabBuildDto dto)
    {
        Result<string> info = validator.ValidatePath(Path.Combine(outputRoot, "build.json"), [paths.Builds]);
        Result<string> latest = validator.ValidatePath(Path.Combine(paths.Builds, "latest.txt"), [paths.Builds]);
        if (info.IsFailure || latest.IsFailure)
        {
            return Result.Failure("[Lab.Io] build outputs are not valid paths");
        }

        try
        {
            Directory.CreateDirectory(outputRoot);
            // JsonObject rather than an anonymous type: serialization stays trim-safe under the CI analyzers.
            var json = new JsonObject
            {
                ["build_id"] = dto.BuildId,
                ["commit"] = dto.Commit,
                ["dirty"] = dto.Dirty,
                ["checkout"] = dto.Checkout,
                ["projects"] = new JsonArray(dto.Projects.Select(name => (JsonNode?)name).ToArray()),
                ["images"] = new JsonArray(dto.Images.Select(image => (JsonNode?)image).ToArray()),
                ["at"] = dto.At.ToString("O", CultureInfo.InvariantCulture),
            };
            File.WriteAllText(info.Value!, json.ToJsonString());
            File.WriteAllText(latest.Value!, dto.BuildId);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] build outputs could not be written");
        }
    }

    /// <summary>
    /// Runs one child process through <c>ArgumentList</c> with redirected output. Returns stdout on exit code 0; otherwise a
    /// failure carrying stderr-derived lock-drift text only when present (never the environment or the full command line).
    /// </summary>
    internal static async Task<Result<string>> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct)
    {
        using var process = new Process();
        ProcessStartInfo info = process.StartInfo;
        info.FileName = fileName;
        info.WorkingDirectory = workingDirectory;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        LabProcessStart.StripToken(info);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProcessTimeout);
        try
        {
            if (!process.Start())
            {
                return Result<string>.Failure($"[Lab.ProcessFailed] {fileName} did not start");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string output = await stdout.ConfigureAwait(false);
            string errors = await stderr.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return Result<string>.Success(output);
            }

            return IsLockDrift(errors + output)
                ? Result<string>.Failure(LockDriftError)
                : Result<string>.Failure($"[Lab.ProcessFailed] {fileName} exited with code {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            // Either the build's own timeout or the caller's cancellation: the whole tree (MSBuild nodes included) is stopped.
            KillQuietly(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return Result<string>.Failure($"[Lab.Timeout] {fileName} did not finish in time");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result<string>.Failure($"[Lab.ProcessFailed] {fileName} could not run");
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }
}

/// <summary>
/// One completed <c>lab_build</c>: the build id, commit, dirty flag, checkout and the images it produced (L09).
/// </summary>
public sealed record LabBuildDto(
    string BuildId,
    string Commit,
    bool Dirty,
    string Checkout,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Images,
    DateTimeOffset At);
