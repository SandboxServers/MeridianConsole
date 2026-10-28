namespace Dhadgar.Lab.Tests.Triage;

/// <summary>
/// Nothing in the lab product code files an issue or talks to GitHub (D-ML28). The scan fails if anything starts to.
/// </summary>
public sealed class NoGitHubCallsTests
{
    [Fact]
    public void LabSources_NeverCallGitHub()
    {
        string source = FindLabSourceFolder();
        string[] files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(files);

        var hits = new List<string>();
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string banned in new[] { "api.github.com", "Octokit", "\"gh\"", "\"gh.exe\"" })
            {
                if (text.Contains(banned, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(Path.GetFileName(file) + ": " + banned);
                }
            }
        }

        Assert.Empty(hits);
    }

    private static string FindLabSourceFolder()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "src", "Tools", "Dhadgar.Lab");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("src/Tools/Dhadgar.Lab was not found above the test output folder");
    }
}
