using Dhadgar.Mods.Services;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Mods.Tests;

public class VersionRangeParserTests
{
    // ── Caret (^) ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.5.0", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.0.0", false)]
    [InlineData("0.9.9", false)]
    [InlineData("1.2.2", false)]
    public void Satisfies_CaretNormalMajor_LocksToMajorVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "^1.2.3");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.2.3", true)]
    [InlineData("0.2.9", true)]
    [InlineData("0.3.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("0.2.2", false)]
    public void Satisfies_CaretZeroMajor_LocksToMinorVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "^0.2.3");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.0.3", true)]
    [InlineData("0.0.4", false)]
    [InlineData("0.0.2", false)]
    [InlineData("0.1.0", false)]
    public void Satisfies_CaretZeroMinor_LocksToPatchVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "^0.0.3");

        result.Should().Be(expected);
    }

    // ── Tilde (~) ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.9", true)]
    [InlineData("1.3.0", false)]
    [InlineData("1.2.2", false)]
    [InlineData("2.0.0", false)]
    public void Satisfies_Tilde_LocksToMinorVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "~1.2.3");

        result.Should().Be(expected);
    }

    // ── Comparison Operators ───────────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("0.9.9", false)]
    public void Satisfies_GreaterThanOrEqual_WorksCorrectly(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, ">=1.0.0");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("2.0.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.1", false)]
    public void Satisfies_LessThanOrEqual_WorksCorrectly(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "<=2.0.0");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0.1", true)]
    [InlineData("2.0.0", true)]
    [InlineData("1.0.0", false)]
    [InlineData("0.9.9", false)]
    public void Satisfies_GreaterThan_WorksCorrectly(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, ">1.0.0");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.9.9", true)]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", false)]
    [InlineData("2.0.1", false)]
    public void Satisfies_LessThan_WorksCorrectly(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "<2.0.0");

        result.Should().Be(expected);
    }

    // ── Exact Match ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.4", false)]
    [InlineData("1.2.2", false)]
    public void Satisfies_ExactMatch_OnlyMatchesExactVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "1.2.3");

        result.Should().Be(expected);
    }

    // ── Wildcard ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.0", true)]
    [InlineData("1.2.9", true)]
    [InlineData("1.3.0", false)]
    [InlineData("2.2.0", false)]
    public void Satisfies_WildcardPatch_MatchesMinorVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "1.2.*");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.5.9", true)]
    [InlineData("2.0.0", false)]
    public void Satisfies_WildcardMinor_MatchesMajorVersion(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, "1.*");

        result.Should().Be(expected);
    }

    [Fact]
    public void Satisfies_WildcardAll_MatchesAnyVersion()
    {
        var sv = SemanticVersion.Parse("99.88.77");

        var result = VersionRangeParser.Satisfies(sv, "*");

        result.Should().BeTrue();
    }

    // ── Compound Ranges ────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.5.0", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.0.0", false)]
    [InlineData("0.9.9", false)]
    public void Satisfies_CompoundRange_BothConstraintsMustMatch(string version, bool expected)
    {
        var sv = SemanticVersion.Parse(version);

        var result = VersionRangeParser.Satisfies(sv, ">=1.0.0 <2.0.0");

        result.Should().Be(expected);
    }

    // ── Edge Cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Satisfies_InvalidConstraint_ReturnsFalse()
    {
        var sv = SemanticVersion.Parse("1.0.0");

        var result = VersionRangeParser.Satisfies(sv, "not-a-constraint");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Satisfies_EmptyOrWhitespace_ReturnsTrue(string? constraint)
    {
        var sv = SemanticVersion.Parse("1.0.0");

        var result = VersionRangeParser.Satisfies(sv, constraint!);

        result.Should().BeTrue();
    }
}
