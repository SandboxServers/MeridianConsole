using System.Diagnostics;
using System.Formats.Tar;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;

// The child-process environment scrub and the patch scope check are tested from Dhadgar.Lab.Tests (pass 2, N2 and N3).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Dhadgar.Lab.Tests")]

namespace Dhadgar.Lab.SelfCheck;

/// <summary>
/// The mutant loop of <c>selfcheck</c> (L55). Archives HEAD into a temporary copy, runs a baseline over the named tests,
/// then for each lab mutant applies its patch, builds, runs its named test, judges the TRX and reverts the patch. Every
/// process runs with <c>ArgumentList</c>. Nothing touches the checkout.
/// </summary>
public static class SelfCheckRunner
{
    /// <summary>The directory under the temp folder that holds every copy (R-PATH).</summary>
    public const string TempFolderName = "meridian-lab-selfcheck";

    /// <summary>The file names written under <c>results\selfcheck\&lt;timestamp&gt;\</c>.</summary>
    public const string JsonFileName = "selfcheck.json";

    /// <summary>The Markdown report file name.</summary>
    public const string MarkdownFileName = "selfcheck.md";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Runs the mutant loop and writes the report. Returns the rows; the caller decides the exit code.
    /// </summary>
    /// <param name="options">The parsed command line.</param>
    /// <param name="repoRoot">The repository root whose HEAD is archived.</param>
    /// <param name="paths">The lab layout; the report goes under <c>Results\selfcheck</c>.</param>
    /// <param name="validator">The path validator for every path used.</param>
    /// <param name="time">The clock for the report folder name.</param>
    /// <param name="ct">Cancels the run.</param>
    public static async Task<Result<IReadOnlyList<SelfCheckRow>>> RunAsync(
        SelfCheckOptions options,
        string repoRoot,
        LabPaths paths,
        IPathValidator validator,
        TimeProvider time,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(time);

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(Path.Combine(repoRoot, "tools", "lab", "mutants"), repoRoot, validator);
        if (specs.IsFailure)
        {
            return Result<IReadOnlyList<SelfCheckRow>>.Failure(specs.Error);
        }

        List<MutantSpec> labs = specs.Value!.Where(s => s.Kind == MutantKind.Lab && (options.Only is null || s.Id == options.Only)).ToList();
        if (labs.Count == 0)
        {
            return Result<IReadOnlyList<SelfCheckRow>>.Failure("[Lab.NotFound] no lab mutant matches the request");
        }

        string tempRoot = Path.GetTempPath();
        Result<string> workRoot = validator.ValidatePath(Path.Combine(tempRoot, TempFolderName, Guid.NewGuid().ToString("N")), [tempRoot]);
        if (workRoot.IsFailure)
        {
            return Result<IReadOnlyList<SelfCheckRow>>.Failure("[Lab.InvalidArgument] the temporary folder is not valid");
        }

        IReadOnlyList<SelfCheckRow> rows;
        try
        {
            rows = await JudgeAllAsync(labs, repoRoot, workRoot.Value!, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Result<IReadOnlyList<SelfCheckRow>>.Failure("[Lab.ProcessFailed] the self-check copy could not be prepared");
        }
        finally
        {
            if (!options.Keep)
            {
                DeleteQuietly(workRoot.Value!);
            }
        }

        Result<string> written = WriteReport(rows, paths, validator, time);
        return written.IsFailure
            ? Result<IReadOnlyList<SelfCheckRow>>.Failure(written.Error)
            : Result<IReadOnlyList<SelfCheckRow>>.Success(rows);
    }

    /// <summary>Finds the repository root: the folder above the binary that holds <c>Dhadgar.sln</c>.</summary>
    public static string? FindRepoRoot(string start)
    {
        DirectoryInfo? dir = new(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Dhadgar.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task<IReadOnlyList<SelfCheckRow>> JudgeAllAsync(List<MutantSpec> labs, string repoRoot, string workRoot, CancellationToken ct)
    {
        string copy = Path.Combine(workRoot, "copy");
        string trxDir = Path.Combine(workRoot, "trx");
        Directory.CreateDirectory(trxDir);
        await ExtractHeadAsync(repoRoot, workRoot, copy, ct).ConfigureAwait(false);

        string csproj = Path.Combine(copy, "tests", "Dhadgar.Lab.Tests", "Dhadgar.Lab.Tests.csproj");
        (int buildExit, _) = await RunProcessAsync("dotnet", ["build", csproj, "-c", "Release"], null, ct).ConfigureAwait(false);
        if (buildExit != 0)
        {
            return SelfCheckJudge.AllInvalid(labs.Select(m => (m.Id, m.Detector.Test!)), "baseline build failed; nothing was judged");
        }

        string filter = string.Join("|", labs.Select(m => "FullyQualifiedName=" + m.Detector.Test));
        IReadOnlyList<TrxTestResult> baseline = await RunTestsAsync(csproj, filter, "baseline", trxDir, ct).ConfigureAwait(false);
        foreach (MutantSpec mutant in labs)
        {
            string? problem = SelfCheckJudge.BaselineProblem(mutant.Detector.Test!, baseline);
            if (problem is not null)
            {
                return SelfCheckJudge.AllInvalid(labs.Select(m => (m.Id, m.Detector.Test!)), problem);
            }
        }

        var rows = new List<SelfCheckRow>();
        foreach (MutantSpec mutant in labs)
        {
            rows.Add(await JudgeOneAsync(mutant, copy, csproj, trxDir, ct).ConfigureAwait(false));
        }

        return rows;
    }

    /// <summary>Archives HEAD with <c>git archive</c> into a tar under <paramref name="workRoot"/> and extracts it into <paramref name="copy"/>.</summary>
    /// <exception cref="InvalidOperationException">The archive step failed.</exception>
    internal static async Task ExtractHeadAsync(string repoRoot, string workRoot, string copy, CancellationToken ct)
    {
        string tar = Path.Combine(workRoot, "head.tar");
        (int archiveExit, string archiveOut) = await RunProcessAsync("git", ["-C", repoRoot, "archive", "--format=tar", "--output=" + tar, "HEAD"], null, ct).ConfigureAwait(false);
        if (archiveExit != 0)
        {
            throw new InvalidOperationException("git archive failed: " + archiveOut);
        }

        Directory.CreateDirectory(copy);
        using (FileStream stream = File.OpenRead(tar))
        {
            await TarFile.ExtractToDirectoryAsync(stream, copy, overwriteFiles: true, ct).ConfigureAwait(false);
        }

        File.Delete(tar);
    }

    private static async Task<SelfCheckRow> JudgeOneAsync(MutantSpec mutant, string copy, string csproj, string trxDir, CancellationToken ct)
    {
        string test = mutant.Detector.Test!;
        string patch = Path.Combine(mutant.Folder, MutantSpecLoader.PatchFileName);

        // The same scope check the product path runs (S-N2 pass 2): a patch that names a file its spec does not list is refused
        // before git apply, so a build file cannot reach dotnet build through a lab mutant.
        SelfCheckRow? scope = PatchScopeRow(mutant);
        if (scope is not null)
        {
            return scope;
        }

        (int applyExit, _) = await RunProcessAsync("git", ["-c", "core.longpaths=true", "-C", copy, "apply", patch], null, ct).ConfigureAwait(false);
        if (applyExit != 0)
        {
            return new SelfCheckRow(mutant.Id, test, SelfCheckJudge.Invalid, "the patch does not apply; refresh it as tools/lab/mutants/README.md says");
        }

        SelfCheckRow row;
        bool reverted;
        try
        {
            row = await BuildAndJudgeAsync(mutant, csproj, trxDir, ct).ConfigureAwait(false);
        }
        finally
        {
            // The revert runs even when the build or the run throws, so the next mutant starts from a clean copy.
            (int revertExit, _) = await RunProcessAsync("git", ["-c", "core.longpaths=true", "-C", copy, "apply", "-R", patch], null, CancellationToken.None).ConfigureAwait(false);
            reverted = revertExit == 0;
        }

        return reverted ? row : throw new InvalidOperationException("the patch could not be reverted");
    }

    /// <summary>
    /// The Invalid row when the mutant's patch touches a file its spec does not list, or when the patch cannot be read; null when
    /// the scope check passes. Runs before any <c>git apply</c>.
    /// </summary>
    internal static SelfCheckRow? PatchScopeRow(MutantSpec mutant)
    {
        string patch = Path.Combine(mutant.Folder, MutantSpecLoader.PatchFileName);
        string[] lines;
        try
        {
            lines = File.ReadAllLines(patch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SelfCheckRow(mutant.Id, mutant.Detector.Test!, SelfCheckJudge.Invalid, "the patch could not be read");
        }

        Result scope = MutantBuilder.PatchTouchesOnly(lines, mutant.Files);
        return scope.IsFailure
            ? new SelfCheckRow(mutant.Id, mutant.Detector.Test!, SelfCheckJudge.Invalid, scope.Error)
            : null;
    }

    private static async Task<SelfCheckRow> BuildAndJudgeAsync(MutantSpec mutant, string csproj, string trxDir, CancellationToken ct)
    {
        string test = mutant.Detector.Test!;
        (int buildExit, _) = await RunProcessAsync("dotnet", ["build", csproj, "-c", "Release"], null, ct).ConfigureAwait(false);
        if (buildExit != 0)
        {
            return SelfCheckJudge.Judge(mutant.Id, test, built: false, []);
        }

        IReadOnlyList<TrxTestResult> rows = await RunTestsAsync(csproj, "FullyQualifiedName=" + test, mutant.Id, trxDir, ct).ConfigureAwait(false);
        return SelfCheckJudge.Judge(mutant.Id, test, built: true, rows);
    }

    private static async Task<IReadOnlyList<TrxTestResult>> RunTestsAsync(string csproj, string filter, string name, string trxDir, CancellationToken ct)
    {
        string logName = name + ".trx";
        _ = await RunProcessAsync(
            "dotnet",
            ["test", csproj, "-c", "Release", "--no-build", "--filter", filter, "--logger", "trx;LogFileName=" + logName, "--results-directory", trxDir],
            null,
            ct).ConfigureAwait(false);

        string trxPath = Path.Combine(trxDir, logName);
        if (!File.Exists(trxPath))
        {
            // No TRX: the run produced no results (zero tests, or a host failure). The judge reads that as zero tests run.
            return [];
        }

        Result<IReadOnlyList<TrxTestResult>> parsed = TrxReader.Read(await File.ReadAllTextAsync(trxPath, ct).ConfigureAwait(false));
        return parsed.IsSuccess ? parsed.Value! : [];
    }

    /// <summary>Runs one child process with <c>ArgumentList</c> and a bounded wait; stdout and stderr are captured.</summary>
    internal static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, IReadOnlyList<string> args, string? workingDirectory, CancellationToken ct)
    {
        using var process = new Process();
        ProcessStartInfo info = process.StartInfo;
        info.FileName = fileName;
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        if (workingDirectory is not null)
        {
            info.WorkingDirectory = workingDirectory;
        }

        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;

        // The daemon token never reaches a build, test or git child (N3 pass 2); a mutated test project could read it.
        info.Environment.Remove(LabDefaults.TokenEnvVar);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "the process did not start");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(StepTimeout);
        try
        {
            await process.WaitForExitAsync(bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            return (-1, "the process timed out or was cancelled");
        }

        var text = new StringBuilder();
        text.Append(await stdout.ConfigureAwait(false));
        text.Append(await stderr.ConfigureAwait(false));
        return (process.ExitCode, text.ToString());
    }

    private static void KillTree(Process process)
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
            // The process exited between the check and the kill.
        }
    }

    internal static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Left behind for the operator; the report still names the verdicts.
        }
        catch (UnauthorizedAccessException)
        {
            // Left behind for the operator; the report still names the verdicts.
        }
    }

    private static Result<string> WriteReport(IReadOnlyList<SelfCheckRow> rows, LabPaths paths, IPathValidator validator, TimeProvider time)
    {
        string stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        Result<string> dir = validator.ValidatePath(Path.Combine(paths.Results, "selfcheck", stamp), [paths.Results]);
        if (dir.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the report folder is not valid");
        }

        try
        {
            Directory.CreateDirectory(dir.Value!);
            string json = JsonSerializer.Serialize(rows, JsonOptions);
            File.WriteAllText(Path.Combine(dir.Value!, JsonFileName), json);
            File.WriteAllText(Path.Combine(dir.Value!, MarkdownFileName), RenderMarkdown(rows, stamp));
            return Result<string>.Success(dir.Value!);
        }
        catch (IOException)
        {
            return Result<string>.Failure("[Lab.Io] the self-check report could not be written");
        }
    }

    /// <summary>Renders <c>selfcheck.md</c>: one row per mutant and the caught count.</summary>
    public static string RenderMarkdown(IReadOnlyList<SelfCheckRow> rows, string stamp)
    {
        int caught = rows.Count(r => r.Verdict == SelfCheckJudge.Caught);
        var text = new StringBuilder();
        text.AppendLine("# Lab self-check " + stamp);
        text.AppendLine();
        text.AppendLine($"Caught {caught} of {rows.Count}. The gate passes only when every lab mutant is caught.");
        text.AppendLine();
        text.AppendLine("| Mutant | Test | Verdict | Detail |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (SelfCheckRow row in rows)
        {
            text.AppendLine($"| {row.Id} | {row.Test} | {row.Verdict} | {row.Detail} |");
        }

        return text.ToString();
    }
}
