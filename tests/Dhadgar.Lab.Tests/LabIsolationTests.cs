namespace Dhadgar.Lab.Tests;

/// <summary>
/// Keeps the lab out of product builds and deployments. Walks up from the test binary to Dhadgar.sln and scans the repo.
/// </summary>
public sealed class LabIsolationTests
{
    private static readonly string[] ProductScanRoots = ["deploy", Path.Combine("src", "Dhadgar.AppHost"), Path.Combine(".github", "workflows")];

    [Fact]
    public void LabIsolation_ProductProjectsDoNotReferenceLab()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var violations = ScanForCsprojViolations(root);
        Assert.Empty(violations);
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

    private static List<string> ScanForCsprojViolations(string root)
    {
        var violations = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
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

        candidates.AddRange(Directory.EnumerateFiles(root, "Dockerfile*", SearchOption.AllDirectories));
        candidates.AddRange(Directory.EnumerateFiles(root, "*.Dockerfile", SearchOption.AllDirectories));

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
