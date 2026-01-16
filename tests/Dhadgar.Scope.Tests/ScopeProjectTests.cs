using Xunit;

namespace Dhadgar.Scope.Tests;

/// <summary>
/// Tests for the Dhadgar.Scope Astro documentation site.
/// Note: Dhadgar.Scope is now a Node.js/Astro project.
/// For comprehensive E2E testing, consider adding Playwright tests.
/// </summary>
public class ScopeProjectTests
{
    private static readonly string ScopeProjectDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Dhadgar.Scope");

    [Fact]
    public void Package_json_exists()
    {
        var packageJson = Path.Combine(ScopeProjectDir, "package.json");
        Assert.True(File.Exists(packageJson), $"package.json should exist at {packageJson}");
    }

    [Fact]
    public void Astro_config_exists()
    {
        var astroConfig = Path.Combine(ScopeProjectDir, "astro.config.mjs");
        Assert.True(File.Exists(astroConfig), $"astro.config.mjs should exist at {astroConfig}");
    }

    [Fact]
    public void Section_content_files_exist()
    {
        var sectionsDir = Path.Combine(ScopeProjectDir, "src", "sections");
        Assert.True(Directory.Exists(sectionsDir), $"Sections directory should exist at {sectionsDir}");

        var astroFiles = Directory.GetFiles(sectionsDir, "*.astro");
        Assert.True(astroFiles.Length >= 19, $"Expected at least 19 section files, found {astroFiles.Length}");
    }

    [Fact]
    public void Content_json_files_exist()
    {
        var contentDir = Path.Combine(ScopeProjectDir, "public", "content");
        Assert.True(Directory.Exists(contentDir), $"Content directory should exist at {contentDir}");

        var expectedFiles = new[] { "dependencies.json", "architecture-park.v1.json", "db-schemas.v1.json", "comm-matrix.v1.json" };
        foreach (var file in expectedFiles)
        {
            var filePath = Path.Combine(contentDir, file);
            Assert.True(File.Exists(filePath), $"{file} should exist at {filePath}");
        }
    }
}
