namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Walks the repository for the text-scanning guards in this test project (VS-32R). Build output, dependency folders,
/// the test tree and documentation are skipped, so a scan sees only the files a deployed service or pipeline can read.
/// </summary>
internal static class RepoFiles
{
    private static readonly string[] SkippedAnywhere = ["bin", "obj", "node_modules", ".git"];
    private static readonly string[] SkippedAtRoot = [".claude", "docs", "tests"];
    private static readonly string[] ScannedExtensions = [".yml", ".yaml", ".json", ".cs", ".props", ".targets", ".csproj", ".sh", ".ps1", ".cmd", ".env"];

    /// <summary>
    /// Finds the folder that holds <c>Dhadgar.sln</c>, walking up from the test output directory.
    /// </summary>
    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Dhadgar.sln not found above the test output directory.");
    }

    /// <summary>
    /// True when a file under <paramref name="root"/> is one that deploy, pipeline or build configuration can carry:
    /// every file under <c>deploy/</c> or <c>.github/</c>, and any text file with a scanned extension or Dockerfile name.
    /// </summary>
    internal static bool IsScanned(string root, string file)
    {
        if (IsUnder(root, file, "deploy") || IsUnder(root, file, ".github"))
        {
            return true;
        }

        var fileName = Path.GetFileName(file);
        if (fileName.StartsWith("Dockerfile", StringComparison.Ordinal) || fileName.StartsWith(".env", StringComparison.Ordinal))
        {
            return true;
        }

        return ScannedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="file"/> sits under the top-level <paramref name="folder"/> of <paramref name="root"/>.
    /// </summary>
    internal static bool IsUnder(string root, string file, string folder) =>
        Path.GetRelativePath(root, file).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>
    /// Lists every file under <paramref name="root"/> that passes <paramref name="includeFile"/>, pruning the
    /// skipped folders while walking so <c>node_modules</c> and <c>.git</c> are never enumerated.
    /// </summary>
    internal static IEnumerable<string> Enumerate(string root, Func<string, bool> includeFile)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(subdirectory);
                if (SkippedAnywhere.Contains(name, StringComparer.Ordinal)
                    || (directory == root && SkippedAtRoot.Contains(name, StringComparer.Ordinal)))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (includeFile(file))
                {
                    yield return file;
                }
            }
        }
    }
}
