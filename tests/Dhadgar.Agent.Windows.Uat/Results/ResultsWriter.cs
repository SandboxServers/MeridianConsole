using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>
/// Writes <c>results.json</c>, <c>results.md</c> and <c>cleanup-results.json</c>. Each file is written to a temp file
/// and then moved over the target, so a reader never sees a half-written result. Every string in them passes
/// through the run's <see cref="Redactor"/> first (R-LOG), so a secret echoed by a step or an exception never lands in a file.
/// </summary>
internal static class ResultsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Writes <c>results.json</c> and <c>results.md</c> into <paramref name="runDir"/>.</summary>
    public static Result Write(UatResults results, string runDir, Redactor redactor)
    {
        var node = JsonSerializer.SerializeToNode(results, JsonOptions);
        RedactInPlace(node, redactor);
        var json = WriteAtomically(Path.Combine(runDir, "results.json"), node?.ToJsonString(JsonOptions) ?? "null");
        if (json.IsFailure)
        {
            return json;
        }

        return WriteAtomically(Path.Combine(runDir, "results.md"), redactor.Redact(ToMarkdown(results)));
    }

    /// <summary>Writes the cleanup object to <c>cleanup-results.json</c> (the <c>cleanup</c> verb).</summary>
    public static Result WriteCleanup(CleanupSummary cleanup, string runDir, Redactor redactor)
    {
        var node = JsonSerializer.SerializeToNode(cleanup, JsonOptions);
        RedactInPlace(node, redactor);
        return WriteAtomically(Path.Combine(runDir, "cleanup-results.json"), node?.ToJsonString(JsonOptions) ?? "null");
    }

    /// <summary>Replaces every string value in the tree with its redacted form. Object keys are left as they are.</summary>
    private static void RedactInPlace(JsonNode? node, Redactor redactor)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        obj[key] = JsonValue.Create(redactor.Redact(text));
                    }
                    else
                    {
                        RedactInPlace(child, redactor);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        array[i] = JsonValue.Create(redactor.Redact(text));
                    }
                    else
                    {
                        RedactInPlace(child, redactor);
                    }
                }

                break;
        }
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Windows agent UAT {r.RunId}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Verb: {r.Verb}{(r.Mode is null ? string.Empty : $" (mode {r.Mode})")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Overall: **{r.Overall}** (exit {r.ExitCode}); cleanup: {r.Cleanup.Status}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Git: {r.GitSha}{(r.GitDirty ? " (dirty)" : string.Empty)}; Windows build {r.WindowsBuild}; .NET {r.DotnetVersion}");
        sb.AppendLine(r.AgentRun is null
            ? $"- Launched by: {r.LaunchedBy}; elevated: {r.Elevated}"
            : $"- Launched by: agent (D-S239), tree clean: {r.AgentRun.TreeClean}; elevated: {r.Elevated}");
        sb.AppendLine();
        sb.AppendLine("| Step | Name | Status | Evidence | Duration |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var step in r.Steps)
        {
            // The msi evidence object is listed under the table instead (P23).
            var evidence = string.Join("; ", step.Evidence.Where(kv => kv.Key != "msi").Select(kv => $"{kv.Key}={kv.Value}"));
            var detail = string.IsNullOrEmpty(step.Message) ? evidence : $"{step.Message}; {evidence}".TrimEnd(' ', ';');
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {step.Id} | {step.Name} | {step.Status} | {Cell(detail)} | {step.DurationMs} ms |");
        }

        if (r.Msi is not null)
        {
            sb.AppendLine();
            sb.AppendLine("MSI:");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"- Product code: {r.Msi.ProductCode ?? "none"}; version: {r.Msi.ProductVersion ?? "none"}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"- Install exit code: {r.Msi.InstallExitCode}; uninstall exit code: {r.Msi.UninstallExitCode}");
            sb.AppendLine(r.Msi.Residue.Count == 0
                ? "- Residue: none"
                : "- Residue: " + string.Join("; ", r.Msi.Residue));
        }

        return sb.ToString();
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
