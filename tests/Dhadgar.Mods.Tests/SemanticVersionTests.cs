using Dhadgar.Mods.Services;
using Xunit;

namespace Dhadgar.Mods.Tests;

/// <summary>
/// Tests for SemanticVersion parsing and comparison.
/// Per semver 2.0 spec: https://semver.org/
/// </summary>
public class SemanticVersionTests
{
    [Fact]
    public void Parse_ValidVersion_ReturnsSemanticVersion()
    {
        var version = SemanticVersion.Parse("1.2.3");

        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Null(version.Prerelease);
        Assert.Null(version.BuildMetadata);
    }

    [Fact]
    public void Parse_WithPrerelease_ReturnsCorrectPrerelease()
    {
        var version = SemanticVersion.Parse("1.0.0-alpha.1");

        Assert.Equal("alpha.1", version.Prerelease);
    }

    [Fact]
    public void Parse_WithBuildMetadata_ReturnsCorrectMetadata()
    {
        var version = SemanticVersion.Parse("1.0.0+build.123");

        Assert.Equal("build.123", version.BuildMetadata);
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("1.0.1", "1.0.2", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    public void CompareTo_MajorMinorPatch_ComparesCorrectly(string left, string right, int expected)
    {
        var leftVersion = SemanticVersion.Parse(left);
        var rightVersion = SemanticVersion.Parse(right);

        var result = Math.Sign(leftVersion.CompareTo(rightVersion));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompareTo_PrereleaseHasLowerPrecedenceThanRelease()
    {
        var prerelease = SemanticVersion.Parse("1.0.0-alpha");
        var release = SemanticVersion.Parse("1.0.0");

        Assert.True(prerelease < release);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1, "fewer identifiers < more identifiers")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1, "numeric < alphanumeric")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", -1, "numeric comparison: 1 < 2")]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1, "numeric comparison: 2 < 10 (not string!)")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta", -1, "lexical: alpha.beta < beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2", -1, "fewer identifiers < more identifiers")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1, "numeric: 2 < 11")]
    [InlineData("1.0.0-rc.1", "1.0.0", -1, "prerelease < release")]
    public void CompareTo_PrereleaseOrdering_FollowsSemverSpec(string left, string right, int expected, string _)
    {
        var leftVersion = SemanticVersion.Parse(left);
        var rightVersion = SemanticVersion.Parse(right);

        var result = Math.Sign(leftVersion.CompareTo(rightVersion));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompareTo_SemverExampleOrder_IsCorrect()
    {
        // From semver.org: 1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0
        var versions = new[]
        {
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0"
        };

        var parsed = versions.Select(SemanticVersion.Parse).ToList();

        // Verify each version is less than the next
        for (var i = 0; i < parsed.Count - 1; i++)
        {
            Assert.True(parsed[i] < parsed[i + 1],
                $"Expected {versions[i]} < {versions[i + 1]}");
        }
    }

    [Fact]
    public void CompareTo_NumericVsAlphanumeric_NumericIsLower()
    {
        // Per semver spec: numeric identifiers always have lower precedence than alphanumeric
        var numeric = SemanticVersion.Parse("1.0.0-1");
        var alphanumeric = SemanticVersion.Parse("1.0.0-a");

        Assert.True(numeric < alphanumeric);
    }

    [Fact]
    public void CompareTo_NumericParts_ComparedAsIntegers()
    {
        // "9" < "10" when compared as integers, but "9" > "10" as strings
        var version9 = SemanticVersion.Parse("1.0.0-9");
        var version10 = SemanticVersion.Parse("1.0.0-10");

        Assert.True(version9 < version10);
    }
}
