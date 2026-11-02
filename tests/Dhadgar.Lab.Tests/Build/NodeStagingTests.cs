using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Build;

/// <summary>What the Node build stages and what the docker build argument list is (L34, D-ML144). No Docker.</summary>
public sealed class NodeStagingTests
{
    [Fact]
    public void NodeDockerBuildArgs_IsTheExactList()
    {
        IReadOnlyList<string> args = LabBuilder.NodeDockerBuildArgs("t", "dist/server/entry.mjs", "ctx", "df");

        Assert.Equal(
            ["build", "--build-arg", "APP_DIR=app", "--build-arg", "APP_ENTRY=dist/server/entry.mjs", "--label", "meridian-lab=1", "-f", "df", "-t", "t", "ctx"],
            args);
    }

    [Fact]
    public void StageNodeApp_CopiesSourceOnly()
    {
        string temp = Path.Combine(Path.GetTempPath(), "meridian-lab-stage-" + Guid.NewGuid().ToString("N"));
        string checkout = Path.Combine(temp, "checkout");
        string home = Path.Combine(temp, "home");
        try
        {
            string app = Path.Combine(checkout, "src", "Dhadgar.Fake");
            Write(app, "package.json");
            Write(app, Path.Combine("src", "a.ts"));
            foreach (string excluded in new[] { ".env", ".env.local", ".npmrc", "key.pem", "cert.pfx", "secrets.json", Path.Combine("node_modules", "x.js"), Path.Combine("dist", "y.js"), Path.Combine("bin", "z.dll"), Path.Combine("obj", "w.json") })
            {
                Write(app, excluded);
            }

            Write(Path.Combine(checkout, "src", "Dhadgar.SharedAuth"), Path.Combine("src", "index.ts"));
            Write(Path.Combine(checkout, "tools", "lab", "images"), "lab-secrets-stub.mjs");
            _ = Directory.CreateDirectory(home);
            LabPaths paths = LabPaths.FromRoot(home, checkout);

            Result<string> staged = LabBuilder.StageNodeApp(
                new BuildProject("fake", "src/Dhadgar.Fake/package.json", string.Empty, "src/Dhadgar.Fake", "src/a.ts"),
                checkout,
                Path.Combine(paths.Builds, "b1"),
                paths,
                new PathValidator(NullLogger<PathValidator>.Instance));

            Assert.True(staged.IsSuccess, staged.IsFailure ? staged.Error : string.Empty);
            var files = Directory.EnumerateFiles(staged.Value!, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(staged.Value!, file).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(
                new HashSet<string>(StringComparer.Ordinal) { "app/package.json", "app/src/a.ts", "SharedAuth/src/index.ts", "lab/secrets-stub.mjs" },
                files);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the temp folder is disposable.
            }
        }
    }

    [Theory]
    [InlineData(".env", false)]
    [InlineData(".env.example", false)]
    [InlineData(".npmrc", false)]
    [InlineData(".yarnrc.yml", false)]
    [InlineData(".dev.vars", false)]
    [InlineData("secrets.json", false)]
    [InlineData("a.PEM", false)]
    [InlineData("a.key", false)]
    [InlineData("a.pfx", false)]
    [InlineData("a.p12", false)]
    [InlineData("package.json", true)]
    [InlineData("index.ts", true)]
    public void IsStagedFile_RefusesCredentialAndKeyNames(string name, bool expected) =>
        Assert.Equal(expected, LabBuilder.IsStagedFile(name));

    private static void Write(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }
}
