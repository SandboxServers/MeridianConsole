using System.Text;
using Dhadgar.Agent.Windows.Uat.Infra;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>What the U9 secret scan found. <see cref="Failures"/> fail U9; <see cref="Warnings"/> are recorded only.</summary>
/// <param name="FilesScanned">Files that existed and were scanned.</param>
/// <param name="Failures">Messages with the file and line, never the secret text.</param>
/// <param name="Warnings">Messages with the file and line for password hits, never the secret text.</param>
internal sealed record SecretScanOutcome(int FilesScanned, IReadOnlyList<string> Failures, IReadOnlyList<string> Warnings);

/// <summary>
/// The U9 secret scan (R-LOG). Every file the run leaves in <c>logs\</c>, <c>results.md</c>, <c>results.json</c> and
/// <c>cleanup-manifest.jsonl</c> is checked with <see cref="Redactor.Scan"/>. A file with a hit is rewritten redacted.
/// An enrollment token hit fails U9. A password hit is a warning only: child logs were redacted on capture, so a hit
/// means a path missed it.
/// </summary>
internal static class SecretScan
{
    /// <summary>Scans and rewrites the run folder's files. Never throws for IO errors; they become failures.</summary>
    public static SecretScanOutcome Run(RunPaths paths, Redactor redactor)
    {
        var files = new List<string>();
        var failures = new List<string>();
        if (Directory.Exists(paths.Logs))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(paths.Logs, "*", SearchOption.AllDirectories));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"logs: could not be listed ({ex.GetType().Name})");
            }
        }

        files.Add(Path.Combine(paths.RunDir, "results.md"));
        files.Add(Path.Combine(paths.RunDir, "results.json"));
        files.Add(paths.Manifest);

        var warnings = new List<string>();
        var scanned = 0;
        foreach (var file in files.Where(File.Exists))
        {
            scanned++;
            var name = Path.GetRelativePath(paths.RunDir, file);
            IReadOnlyList<SecretHit> hits;
            try
            {
                hits = redactor.Scan(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{name}: could not be scanned ({ex.GetType().Name})");
                continue;
            }

            if (hits.Count == 0)
            {
                continue;
            }

            foreach (var hit in hits.Where(h => h.Kind == SecretKind.EnrollmentToken))
            {
                failures.Add($"{name} line {hit.LineNumber}: enrollment token found; file rewritten redacted");
            }

            foreach (var hit in hits.Where(h => h.Kind == SecretKind.Password))
            {
                warnings.Add($"{name} line {hit.LineNumber}: password found; file rewritten redacted (a capture path missed it)");
            }

            var rewritten = Rewrite(file, name, redactor);
            if (rewritten is not null)
            {
                failures.Add(rewritten);
            }
        }

        return new SecretScanOutcome(scanned, failures, warnings);
    }

    /// <summary>Replaces the file's content with its redacted form, through a temp file. Returns a failure message or null.</summary>
    private static string? Rewrite(string file, string name, Redactor redactor)
    {
        var temp = file + ".redacted.tmp";
        try
        {
            File.WriteAllText(temp, redactor.Redact(File.ReadAllText(file)), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, file, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{name}: could not be rewritten redacted ({ex.GetType().Name})";
        }
    }
}
