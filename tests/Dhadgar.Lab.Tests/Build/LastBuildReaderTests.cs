using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Uat.Bundle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Build;

/// <summary>
/// The latest build the lab made, read as a build stamp for the bundle: missing or unsafe files become <see cref="BuildStamp.None"/>.
/// </summary>
public sealed class LastBuildReaderTests : IDisposable
{
    private const string BuildId = "0123abcd";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16f-build-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly LabPaths _paths;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public LastBuildReaderTests()
    {
        _repo = Path.Combine(_root, "repo");
        _paths = LabPaths.FromRoot(_root, _repo);
        _ = Directory.CreateDirectory(_paths.Builds);
    }

    [Fact]
    public void Read_NoLatestFile_IsNone()
    {
        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);

        Assert.Equal(BuildStamp.None, stamp);
    }

    [Fact]
    public void Read_UnsafeId_IsNone()
    {
        File.WriteAllText(Path.Combine(_paths.Builds, "latest.txt"), "..\\x");

        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);

        Assert.Equal(BuildStamp.None, stamp);
    }

    [Fact]
    public void Read_CheckoutUnderRepoRoot_IsRelativeWithSlashes()
    {
        WriteBuild(Path.Combine(_repo, "src", "lab"));

        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);

        Assert.Equal(BuildId, stamp.Id);
        Assert.Equal("src/lab", stamp.Checkout);
        Assert.Equal("0123abcd4567ef89", stamp.Commit);
        Assert.True(stamp.Dirty);
    }

    [Fact]
    public void Read_CheckoutOutsideRepoRoot_IsEmpty()
    {
        WriteBuild(Path.Combine(_root, "elsewhere"));

        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);

        Assert.Equal(BuildId, stamp.Id);
        Assert.Equal(string.Empty, stamp.Checkout);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteBuild(string checkout)
    {
        string dir = Path.Combine(_paths.Builds, BuildId);
        _ = Directory.CreateDirectory(dir);
        var json = new JsonObject
        {
            ["build_id"] = BuildId,
            ["commit"] = "0123abcd4567ef89",
            ["dirty"] = true,
            ["checkout"] = checkout,
        };
        File.WriteAllText(Path.Combine(dir, "build.json"), json.ToJsonString());
        File.WriteAllText(Path.Combine(_paths.Builds, "latest.txt"), BuildId);
    }
}
