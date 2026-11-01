using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Keeps <c>Dhadgar.Testing</c> out of production (VS-32R, security review N3). The library holds <c>SeamJwt</c>, which
/// can make a host accept tokens signed by a process-local key, and it pulls xunit and the test hosts into the build.
/// Nothing else in the build stops a service project from referencing it, so this test does.
/// </summary>
public sealed class TestingLibraryIsolationTests
{
    private const string TestingName = "Dhadgar.Testing";

    [Fact]
    public void ProductionProjects_DoNotReferenceTestingLibrary()
    {
        var root = RepoFiles.FindRepoRoot();
        var projects = RepoFiles.Enumerate(root, file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(file => RepoFiles.IsUnder(root, file, "src") || RepoFiles.IsUnder(root, file, "parked"))
            .ToList();
        Assert.NotEmpty(projects);

        var offenders = projects
            .Where(file => File.ReadAllText(file).Contains(TestingName, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void BuildAndDeployFiles_DoNotNameTestingLibrary()
    {
        var root = RepoFiles.FindRepoRoot();
        var files = RepoFiles.Enumerate(root, file => Path.GetFileName(file).StartsWith("Dockerfile", StringComparison.Ordinal)
                || RepoFiles.IsUnder(root, file, "deploy"))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => File.ReadAllText(file).Contains(TestingName, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(offenders);
    }
}
