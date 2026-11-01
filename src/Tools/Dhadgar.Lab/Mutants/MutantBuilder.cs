using System.Formats.Tar;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Mutants;

/// <summary>
/// Runs one child process for a mutant build: the file name, the <c>ArgumentList</c>, the working directory and the token.
/// <see cref="LabBuilder.RunAsync"/> is the production runner; tests pass a fake.
/// </summary>
public delegate Task<Result<string>> ChildRunner(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct);

/// <summary>
/// One <c>lab_build {mutant}</c>: the verdict (<c>built</c> or <c>stale</c>), the build id, the commit the archive came from
/// and the images produced.
/// </summary>
/// <param name="MutantId">The mutant id.</param>
/// <param name="Verdict"><see cref="MutantBuilder.BuiltVerdict"/> or <see cref="MutantBuilder.StaleVerdict"/>.</param>
/// <param name="Reason">Why the verdict is stale; null when built.</param>
/// <param name="BuildId"><c>mutant-&lt;id&gt;-&lt;build8&gt;</c>.</param>
/// <param name="Commit">The HEAD commit the archive was taken from.</param>
/// <param name="Projects">The projects published.</param>
/// <param name="Images">The image tags built (empty when stale).</param>
/// <param name="At">When the build ran.</param>
public sealed record MutantBuildDto(
    string MutantId,
    string Verdict,
    string? Reason,
    string BuildId,
    string Commit,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Images,
    DateTimeOffset At);

/// <summary>
/// Builds a product mutant (L54, A3, D-S241) in a <c>git archive</c> copy of HEAD under <c>LabPaths.Builds</c>. The layout
/// of one build is <c>Builds\mutant-&lt;id&gt;-&lt;build8&gt;\</c>: <c>archive.tar</c> (deleted after extraction), <c>src\</c>
/// (the extracted tree the patch is applied to) and one publish folder per project. Nothing is built in a checkout.
/// Every <c>git</c>, <c>dotnet</c> and <c>docker</c> call goes through <c>ArgumentList</c>; restore and publish reuse
/// <see cref="LabBuilder.RestoreArgs"/> and <see cref="LabBuilder.PublishArgs"/>, never a runtime identifier.
/// </summary>
public static class MutantBuilder
{
    /// <summary>Verdict when <c>git apply --check</c> accepts the patch and the images were built.</summary>
    public const string BuiltVerdict = "built";

    /// <summary>Verdict when <c>git apply --check</c> rejects the patch at HEAD. Nothing is built.</summary>
    public const string StaleVerdict = "stale";

    /// <summary>Reason text for a stale verdict.</summary>
    public const string StaleReason = "change.patch does not apply at HEAD; refresh it as tools/lab/mutants/README.md says";

    /// <summary>File name of the archive written inside the build folder.</summary>
    public const string ArchiveFileName = "archive.tar";

    /// <summary>Name of the extracted source folder inside the build folder.</summary>
    public const string SourceFolderName = "src";

    /// <summary>Build id: <c>mutant-&lt;id&gt;-&lt;build8&gt;</c>.</summary>
    public static string BuildId(string mutantId, string commit) =>
        $"mutant-{mutantId}-{commit[..8]}";

    /// <summary>Label value every mutant image carries: <c>meridian-lab-mutant=&lt;id&gt;</c> (A2).</summary>
    public static string MutantLabel(string mutantId) => $"meridian-lab-mutant={mutantId}";

    /// <summary>Archive arguments: <c>-C &lt;repo&gt; archive --format=tar --output=&lt;tar&gt; HEAD</c>.</summary>
    public static IReadOnlyList<string> ArchiveArgs(string repoRoot, string tarPath) =>
        ["-C", repoRoot, "archive", "--format=tar", $"--output={tarPath}", "HEAD"];

    /// <summary>
    /// Check arguments: <c>-c core.longpaths=true apply --check &lt;patch&gt;</c>, run in the extracted tree. Git's long-path
    /// support lets a patch apply under a build folder whose paths pass MAX_PATH (wave 5 run 7, cause C).
    /// </summary>
    public static IReadOnlyList<string> ApplyCheckArgs(string patchPath) =>
        ["-c", "core.longpaths=true", "apply", "--check", patchPath];

    /// <summary>
    /// Apply arguments: <c>-c core.longpaths=true apply &lt;patch&gt;</c>, run in the extracted tree. See
    /// <see cref="ApplyCheckArgs"/> for why the long-path flag is passed.
    /// </summary>
    public static IReadOnlyList<string> ApplyArgs(string patchPath) => ["-c", "core.longpaths=true", "apply", patchPath];

    /// <summary>
    /// Builds one product mutant from HEAD of <paramref name="paths"/>'s repo. A patch that does not apply gives the
    /// <see cref="StaleVerdict"/> with nothing built. Any other failure is a <c>[Lab.*]</c> failure.
    /// </summary>
    /// <param name="spec">A product mutant from <see cref="MutantSpecLoader"/>.</param>
    /// <param name="projects">The projects to publish and image (the mutant's <c>services</c> by default).</param>
    /// <param name="paths">The lab layout; the repo root and <c>Builds</c> come from here.</param>
    /// <param name="docker">The docker CLI for <c>docker build</c>.</param>
    /// <param name="validator">The path validator every path goes through.</param>
    /// <param name="time">The clock for the build's timestamp.</param>
    /// <param name="run">The child-process runner (production: <see cref="LabBuilder.RunAsync"/>).</param>
    /// <param name="ct">Cancellation; a cancelled child is killed and the cancellation propagates.</param>
    public static async Task<Result<MutantBuildDto>> BuildAsync(
        MutantSpec spec,
        IReadOnlyList<BuildProject> projects,
        LabPaths paths,
        IDockerCli docker,
        IPathValidator validator,
        TimeProvider time,
        ChildRunner run,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(run);

        if (spec.Kind != MutantKind.Product)
        {
            return Result<MutantBuildDto>.Failure("[Lab.InvalidArgument] only product mutants are built by lab_build");
        }

        if (projects.Count == 0)
        {
            return Result<MutantBuildDto>.Failure("[Lab.InvalidArgument] at least one project is required");
        }

        string repoRoot = paths.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Result<MutantBuildDto>.Failure("[Lab.InvalidArgument] the repo root is not configured");
        }

        Result<string> head = await run("git", ["-C", repoRoot, "rev-parse", "HEAD"], repoRoot, ct).ConfigureAwait(false);
        if (head.IsFailure)
        {
            return Result<MutantBuildDto>.Failure(head.Error);
        }

        string commit = head.Value!.Trim();
        if (commit.Length != 40 || !commit.All(char.IsAsciiHexDigitLower))
        {
            return Result<MutantBuildDto>.Failure("[Lab.ProcessFailed] checkout has no commit");
        }

        DateTimeOffset at = time.GetUtcNow();
        string buildId = BuildId(spec.Id, commit);
        Result<string> outputRoot = validator.ValidatePath(Path.Combine(paths.Builds, buildId), [paths.Builds]);
        if (outputRoot.IsFailure)
        {
            return Result<MutantBuildDto>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        string root = outputRoot.Value!;
        Result<string> tar = validator.ValidatePath(Path.Combine(root, ArchiveFileName), [root]);
        Result<string> source = validator.ValidatePath(Path.Combine(root, SourceFolderName), [root]);
        Result<string> patch = validator.ValidatePath(Path.Combine(spec.Folder, MutantSpecLoader.PatchFileName), [spec.Folder]);
        if (tar.IsFailure || source.IsFailure || patch.IsFailure)
        {
            return Result<MutantBuildDto>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        try
        {
            // A rebuild starts from an empty folder so no file from an earlier extraction survives.
            Result cleared = ClearFolder(root, paths.Builds);
            if (cleared.IsFailure)
            {
                return Result<MutantBuildDto>.Failure(cleared.Error);
            }

            Directory.CreateDirectory(root);
            Result<string> archived = await run("git", ArchiveArgs(repoRoot, tar.Value!), repoRoot, ct).ConfigureAwait(false);
            if (archived.IsFailure)
            {
                return Result<MutantBuildDto>.Failure(archived.Error);
            }

            Result extracted = ExtractArchive(tar.Value!, source.Value!, validator);
            if (extracted.IsFailure)
            {
                return Result<MutantBuildDto>.Failure(extracted.Error);
            }

            // The patch may touch only the files its spec lists (S-N2). Refused before git sees it.
            IReadOnlyList<string> patchLines;
            try
            {
                patchLines = await File.ReadAllLinesAsync(patch.Value!, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result<MutantBuildDto>.Failure("[Lab.Unavailable] change.patch could not be read");
            }

            Result scope = PatchTouchesOnly(patchLines, spec.Files);
            if (scope.IsFailure)
            {
                return Result<MutantBuildDto>.Failure(scope.Error);
            }

            // Outside any repo, git apply acts on plain files under the extracted tree.
            Result<string> check = await run("git", ApplyCheckArgs(patch.Value!), source.Value!, ct).ConfigureAwait(false);
            if (check.IsFailure)
            {
                if (check.Error.StartsWith("[Lab.ProcessFailed] git exited", StringComparison.Ordinal))
                {
                    return Result<MutantBuildDto>.Success(new MutantBuildDto(
                        spec.Id, StaleVerdict, StaleReason, buildId, commit, [], [], at));
                }

                return Result<MutantBuildDto>.Failure(check.Error);
            }

            Result<string> applied = await run("git", ApplyArgs(patch.Value!), source.Value!, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return Result<MutantBuildDto>.Failure("[Lab.ProcessFailed] change.patch did not apply");
            }

            var images = new List<string>();
            var projectNames = new List<string>();
            foreach (BuildProject project in projects)
            {
                Result<string> built = await BuildProjectImageAsync(spec, project, buildId, root, source.Value!, run, docker, validator, ct)
                    .ConfigureAwait(false);
                if (built.IsFailure)
                {
                    return Result<MutantBuildDto>.Failure(built.Error);
                }

                images.Add(built.Value!);
                projectNames.Add(project.Name);
            }

            return Result<MutantBuildDto>.Success(new MutantBuildDto(
                spec.Id, BuiltVerdict, null, buildId, commit, projectNames, images, at));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<MutantBuildDto>.Failure("[Lab.Io] mutant build folder could not be written");
        }
        finally
        {
            DeleteArchiveQuietly(tar.Value!);
        }
    }

    /// <summary>
    /// Restores and publishes one project from the extracted tree, then builds its image with the mutant label. Returns the
    /// image tag.
    /// </summary>
    private static async Task<Result<string>> BuildProjectImageAsync(
        MutantSpec spec,
        BuildProject project,
        string buildId,
        string root,
        string sourceRoot,
        ChildRunner run,
        IDockerCli docker,
        IPathValidator validator,
        CancellationToken ct)
    {
        Result<string> csproj = validator.ValidatePath(Path.Combine(sourceRoot, project.Csproj.Replace('/', Path.DirectorySeparatorChar)), [sourceRoot]);
        Result<string> publishDir = validator.ValidatePath(Path.Combine(root, project.Name), [root]);
        Result<string> dockerfile = validator.ValidatePath(Path.Combine(sourceRoot, "tools", "lab", "images", "dotnet-service.Dockerfile"), [sourceRoot]);
        if (csproj.IsFailure || publishDir.IsFailure || dockerfile.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        Result<string> restore = await run("dotnet", LabBuilder.RestoreArgs(csproj.Value!), sourceRoot, ct).ConfigureAwait(false);
        if (restore.IsFailure)
        {
            return Result<string>.Failure(restore.Error);
        }

        Result<string> publish = await run("dotnet", LabBuilder.PublishArgs(csproj.Value!, publishDir.Value!), sourceRoot, ct).ConfigureAwait(false);
        if (publish.IsFailure)
        {
            return Result<string>.Failure(publish.Error);
        }

        string tag = LabBuilder.ImageTag(project.Name, buildId);
        Result<DockerOutput> image = await docker.RunAsync(
            DockerEngine.Linux,
            LabBuilder.DockerBuildArgs(tag, project.DllName, publishDir.Value!, dockerfile.Value!, MutantLabel(spec.Id)),
            ct).ConfigureAwait(false);
        if (image.IsFailure || image.Value!.ExitCode != 0)
        {
            return Result<string>.Failure("[Lab.Docker] docker build failed");
        }

        return Result<string>.Success(tag);
    }

    /// <summary>
    /// Checks that a patch touches only the files its spec lists (S-N2). Every path in a <c>diff --git</c>, <c>---</c> or
    /// <c>+++</c> header must equal an entry of <paramref name="files"/>; <c>/dev/null</c> is skipped. A rename, copy, mode
    /// change or binary patch is refused, because its header names paths the header check could miss.
    /// </summary>
    public static Result PatchTouchesOnly(IReadOnlyList<string> patchLines, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(patchLines);
        ArgumentNullException.ThrowIfNull(files);

        var allowed = new HashSet<string>(files.Select(f => f.Replace('\\', '/')), StringComparer.Ordinal);
        foreach (string line in patchLines)
        {
            if (line.StartsWith("rename from", StringComparison.Ordinal)
                || line.StartsWith("rename to", StringComparison.Ordinal)
                || line.StartsWith("copy from", StringComparison.Ordinal)
                || line.StartsWith("copy to", StringComparison.Ordinal)
                || line.StartsWith("old mode", StringComparison.Ordinal)
                || line.StartsWith("new mode", StringComparison.Ordinal)
                || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                return Result.Failure("[Lab.InvalidArgument] change.patch uses a rename, copy, mode change or binary patch");
            }

            // Fails closed (S-N2 pass 2): a diff --git line must name one listed path on both sides, and a quoted (C-style)
            // name is refused, because the reader below cannot see inside it.
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (!TryReadDiffGitPath(line, out string? gitPath))
                {
                    return Result.Failure("[Lab.InvalidArgument] change.patch has a header the scope check cannot read");
                }

                if (!allowed.Contains(gitPath))
                {
                    return Result.Failure("[Lab.InvalidArgument] change.patch touches a file that files does not list");
                }

                continue;
            }

            if ((line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal))
                && line.Contains('"', StringComparison.Ordinal))
            {
                return Result.Failure("[Lab.InvalidArgument] change.patch has a header the scope check cannot read");
            }

            foreach (string path in HeaderPaths(line))
            {
                if (!allowed.Contains(path))
                {
                    return Result.Failure("[Lab.InvalidArgument] change.patch touches a file that files does not list");
                }
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Reads the path of a <c>diff --git a/P b/P</c> line. False when the line has a quoted name, is not in that form, or names
    /// different paths on the two sides; the caller refuses such a line.
    /// </summary>
    private static bool TryReadDiffGitPath(string line, out string path)
    {
        const string prefix = "diff --git a/";
        path = string.Empty;
        if (line.Contains('"', StringComparison.Ordinal) || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = line[prefix.Length..];
        int split = rest.IndexOf(" b/", StringComparison.Ordinal);
        if (split < 0)
        {
            return false;
        }

        string first = rest[..split];
        string second = rest[(split + 3)..];
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            return false;
        }

        path = first;
        return true;
    }

    /// <summary>The repo-relative paths one <c>---</c> or <c>+++</c> header line names, with <c>a/</c> and <c>b/</c> stripped.</summary>
    private static IEnumerable<string> HeaderPaths(string line)
    {
        string? header = null;
        if (line.StartsWith("--- ", StringComparison.Ordinal))
        {
            header = line[4..];
            if (header == "/dev/null")
            {
                yield break;
            }

            header = header.StartsWith("a/", StringComparison.Ordinal) ? header[2..] : header;
        }
        else if (line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            header = line[4..];
            if (header == "/dev/null")
            {
                yield break;
            }

            header = header.StartsWith("b/", StringComparison.Ordinal) ? header[2..] : header;
        }

        if (header is not null)
        {
            yield return Normalise(header);
        }
    }

    private static string Normalise(string path) => path.Split('\t')[0].Trim().Replace('\\', '/');

    /// <summary>
    /// Extracts a tar into <paramref name="destination"/>. Every entry's full path is checked before anything is written, so an
    /// entry with <c>..</c>, a rooted name or a drive prefix is refused. Only directories and regular files are written; links and
    /// device entries are refused.
    /// </summary>
    internal static Result ExtractArchive(string tarPath, string destination, IPathValidator validator)
    {
        string root = Path.GetFullPath(destination);
        try
        {
            Directory.CreateDirectory(root);
            using FileStream stream = File.OpenRead(tarPath);
            using var reader = new TarReader(stream);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                {
                    continue;
                }

                Result<string> target = ResolveEntry(root, entry.Name, validator);
                if (target.IsFailure)
                {
                    return Result.Failure(target.Error);
                }

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(target.Value!);
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(target.Value!)!);
                        entry.ExtractToFile(target.Value!, overwrite: true);
                        break;
                    default:
                        return Result.Failure("[Lab.InvalidArgument] archive entry type is refused");
                }
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Result.Failure("[Lab.Io] archive could not be extracted");
        }
    }

    private static Result<string> ResolveEntry(string root, string name, IPathValidator validator)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.Contains(':', StringComparison.Ordinal))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] archive entry escapes the build folder");
        }

        string full = Path.GetFullPath(Path.Combine(root, name));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] archive entry escapes the build folder");
        }

        Result<string> validated = validator.ValidatePath(full, [root]);
        return validated.IsFailure
            ? Result<string>.Failure("[Lab.InvalidArgument] archive entry escapes the build folder")
            : Result<string>.Success(validated.Value!);
    }

    /// <summary>
    /// Empties a build folder under <paramref name="builds"/> so a rebuild starts clean. A folder outside <c>Builds</c> is refused.
    /// </summary>
    private static Result ClearFolder(string folder, string builds)
    {
        if (!folder.StartsWith(builds + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("[Lab.InvalidArgument] build path is not valid");
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.Io] mutant build folder could not be cleared");
        }
    }

    private static void DeleteArchiveQuietly(string tarPath)
    {
        try
        {
            if (File.Exists(tarPath))
            {
                File.Delete(tarPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The archive is only a copy of HEAD; a locked file is left for the next clear.
        }
    }
}
