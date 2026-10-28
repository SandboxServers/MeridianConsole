using System.IO.Enumeration;

namespace Dhadgar.Lab.Tests;

/// <summary>
/// Keeps the lab out of product builds and deployments. Walks up from the test binary to Dhadgar.sln and scans the repo.
/// </summary>
public sealed class LabIsolationTests
{
    private static readonly string[] ProductScanRoots = ["deploy", Path.Combine("src", "Dhadgar.AppHost"), Path.Combine(".github", "workflows")];

    // Nested lab worktrees live under .claude/worktrees; skip them and the build and VCS folders during repo-wide scans.
    private static readonly string[] SkippedDirs = [".git", ".claude", "node_modules", "bin", "obj"];

    [Fact]
    public void LabIsolation_ProductProjectsDoNotReferenceLab()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var violations = ScanForCsprojViolations(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void LabIsolation_IgnoresNestedWorktrees()
    {
        var temp = Path.Combine(Path.GetTempPath(), "lab-isolation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "Dhadgar.sln"), string.Empty);
            var worktreeProject = Path.Combine(temp, ".claude", "worktrees", "w", "src", "Tools", "Dhadgar.Lab", "Dhadgar.Lab.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(worktreeProject)!);
            File.WriteAllText(worktreeProject, "<Project><ItemGroup><ProjectReference Include=\"Dhadgar.Lab\" /></ItemGroup></Project>");

            var violations = ScanForCsprojViolations(temp);
            Assert.Empty(violations);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public void LabIsolation_ProductFilesDoNotNameLab()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var violations = ScanForFileViolations(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void LabIsolation_CatchesPlantedReference()
    {
        var temp = Path.Combine(Path.GetTempPath(), "lab-isolation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "deploy"));
            File.WriteAllText(Path.Combine(temp, "deploy", "x.yaml"), "image: meridian-lab\n");
            File.WriteAllText(Path.Combine(temp, "Dhadgar.sln"), string.Empty);

            var violations = ScanForFileViolations(temp);
            Assert.Contains(violations, v => v.EndsWith("x.yaml", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    internal static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dhadgar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> EnumerateRepoFiles(string root, string pattern)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return [.. new FileSystemEnumerable<string>(root, (ref FileSystemEntry e) => e.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !e.IsDirectory && FileSystemName.MatchesWin32Expression(pattern, e.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry e) => Array.IndexOf(SkippedDirs, e.FileName.ToString()) < 0,
        }];
    }

    private static List<string> ScanForCsprojViolations(string root)
    {
        var violations = new List<string>();
        foreach (var csproj in EnumerateRepoFiles(root, "*.csproj"))
        {
            var rel = Path.GetRelativePath(root, csproj);
            if (rel.StartsWith(Path.Combine("src", "Tools") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("tests" + Path.DirectorySeparatorChar + "Dhadgar.Lab.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.ReadAllText(csproj).Contains("Dhadgar.Lab", StringComparison.Ordinal))
            {
                violations.Add(rel);
            }
        }

        return violations;
    }

    private static List<string> ScanForFileViolations(string root)
    {
        var violations = new List<string>();
        var candidates = new List<string>();
        foreach (var scanRoot in ProductScanRoots)
        {
            var dir = Path.Combine(root, scanRoot);
            if (Directory.Exists(dir))
            {
                candidates.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories));
            }
        }

        candidates.AddRange(EnumerateRepoFiles(root, "Dockerfile*"));
        candidates.AddRange(EnumerateRepoFiles(root, "*.Dockerfile"));

        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, file);
            if (rel.StartsWith("tools" + Path.DirectorySeparatorChar + "lab" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("Dhadgar.Lab", StringComparison.Ordinal) || text.Contains("meridian-lab", StringComparison.Ordinal))
            {
                violations.Add(rel);
            }
        }

        return violations;
    }
}
