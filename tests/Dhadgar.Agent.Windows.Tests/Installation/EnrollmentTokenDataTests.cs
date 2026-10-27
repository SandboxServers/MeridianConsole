using Dhadgar.Agent.Windows.Installation;
using Dhadgar.Agent.Windows.InstallerActions;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Installation;

/// <summary>
/// The installer custom action writes where the agent reads (D-S214, D-S245). The CA's file is linked into this
/// project, so this compiles and runs on Linux CI. The live seam is the UAT Msi leg's U3.
/// </summary>
public sealed class EnrollmentTokenDataTests
{
    [Fact]
    public void KeyAndValueName_MatchAgentRegistry()
    {
        Assert.Equal(AgentRegistry.KeyPath, EnrollmentTokenData.KeyPath);
        Assert.Equal(AgentRegistry.EnrollmentTokenValueName, EnrollmentTokenData.ValueName);
    }

    [Theory]
    [InlineData("abcDEF123-_.~")]
    [InlineData("x")]
    public void IsAcceptable_PrintableToken_IsTrue(string token) =>
        Assert.True(EnrollmentTokenData.IsAcceptable(token));

    [Fact]
    public void IsAcceptable_TokenAtMaxLength_IsTrue() =>
        Assert.True(EnrollmentTokenData.IsAcceptable(new string('a', EnrollmentTokenData.MaxLength)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAcceptable_NullOrEmpty_IsFalse(string? token) =>
        Assert.False(EnrollmentTokenData.IsAcceptable(token));

    [Fact]
    public void IsAcceptable_TokenOverMaxLength_IsFalse() =>
        Assert.False(EnrollmentTokenData.IsAcceptable(new string('a', EnrollmentTokenData.MaxLength + 1)));

    [Theory]
    [InlineData("abc\ndef")]
    [InlineData("abc\0def")]
    [InlineData("\tabc")]
    public void IsAcceptable_ControlCharacter_IsFalse(string token) =>
        Assert.False(EnrollmentTokenData.IsAcceptable(token));
}
