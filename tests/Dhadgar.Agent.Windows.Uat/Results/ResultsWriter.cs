using System.Text;
using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>
/// Writes <c>results.json</c> and <c>results.md</c>. Each file is written to a temp file and then moved
/// over the target, so a reader never sees a half-written result.
/// </summary>
internal static class ResultsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Writes <c>results.json</c> and <c>results.md</c> into <paramref name="runDir"/>.</summary>
    public static Result Write(UatResults results, string runDir)
    {
        var json = WriteAtomically(Path.Combine(runDir, "results.json"), JsonSerializer.Serialize(results, JsonOptions));
        if (json.IsFailure)
        {
            return json;
        }

        return WriteAtomically(Path.Combine(runDir, "results.md"), ToMarkdown(results));
    }

    /// <summary>Writes the cleanup object to <c>cleanup-results.json</c> (the <c>cleanup</c> verb).</summary>
    public static Result WriteCleanup(CleanupSummary cleanup, string runDir)
    {
        return WriteAtomically(Path.Combine(runDir, "cleanup-results.json"), JsonSerializer.Serialize(cleanup, JsonOptions));
    }

    private static Result WriteAtomically(string target, string content)
    {
        var temp = target + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, target, overwrite: true);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"could not write {Path.GetFileName(target)}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"could not write {Path.GetFileName(target)}: {ex.Message}");
        }
    }

    private static string ToMarkdown(UatResults r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Windows agent UAT {r.RunId}");
        sb.AppendLine();
        sb.AppendLine($"- Verb: {r.Verb}{(r.Mode is null ? string.Empty : $" (mode {r.Mode})")}");
        sb.AppendLine($"- Overall: **{r.Overall}** (exit {r.ExitCode}); cleanup: {r.Cleanup.Status}");
        sb.AppendLine($"- Git: {r.GitSha}{(r.GitDirty ? " (dirty)" : string.Empty)}; Windows build {r.WindowsBuild}; .NET {r.DotnetVersion}");
        sb.AppendLine($"- Launched by: {r.LaunchedBy}; elevated: {r.Elevated}");
        sb.AppendLine();
        sb.AppendLine("| Step | Name | Status | Evidence | Duration |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var step in r.Steps)
        {
            var evidence = string.Join("; ", step.Evidence.Select(kv => $"{kv.Key}={kv.Value}"));
            var detail = string.IsNullOrEmpty(step.Message) ? evidence : $"{step.Message}; {evidence}".TrimEnd(' ', ';');
            sb.AppendLine($"| {step.Id} | {step.Name} | {step.Status} | {Cell(detail)} | {step.DurationMs} ms |");
        }

        return sb.ToString();
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
