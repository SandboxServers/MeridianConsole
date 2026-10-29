using System.Diagnostics;
using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The recovery verdicts and the child token shape (L55, A2 and A4). Pure, no processes, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryJudgeTests
{
    [Fact]
    public void ChildEnvironment_CarriesTokenHomeAndRepoRoot()
    {
        var info = new ProcessStartInfo();
        info.Environment[Dhadgar.Lab.LabDefaults.TokenEnvVar] = "inherited-token";
        info.Environment[SelfCheckRecovery.RepoRootEnvVar] = "inherited";

        SelfCheckRecovery.ApplyChildEnvironment(info.Environment, "h", "t", "r");

        Assert.Equal("t", info.Environment[Dhadgar.Lab.LabDefaults.TokenEnvVar]);
        Assert.Equal("h", info.Environment[Dhadgar.Lab.LabDefaults.HomeEnvVar]);
        Assert.Equal("r", info.Environment[SelfCheckRecovery.RepoRootEnvVar]);
    }

    [Fact]
    public void Judge_CleanRestart_HasNoProblems()
    {
        IReadOnlyList<string> problems = SelfCheckRecovery.Judge(faultsEmpty: true, leaseHeld: false, i5Problems: []);

        Assert.Empty(problems);
    }

    [Fact]
    public void Judge_I5Problems_AreAppended()
    {
        IReadOnlyList<string> problems = SelfCheckRecovery.Judge(faultsEmpty: true, leaseHeld: false, i5Problems: ["I5 reports an orphan after the restart: orphan-container meridian-lab-x: d"]);

        Assert.Single(problems);
        Assert.Equal("I5 reports an orphan after the restart: orphan-container meridian-lab-x: d", problems[0]);
    }

    [Theory]
    [InlineData(false, false, "a fault survived the restart")]
    [InlineData(true, true, "a lease survived the restart")]
    public void Judge_AnySurvivor_IsReported(bool faultsEmpty, bool leaseHeld, string expected)
    {
        IReadOnlyList<string> problems = SelfCheckRecovery.Judge(faultsEmpty, leaseHeld, i5Problems: []);

        Assert.Single(problems);
        Assert.Equal(expected, problems[0]);
    }

    [Fact]
    public void NewToken_IsBase64UrlOf32Bytes()
    {
        string token = SelfCheckRecovery.NewToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        string padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        Assert.Equal(32, Convert.FromBase64String(padded).Length);
    }

    [Fact]
    public void NewToken_IsFreshEachCall()
    {
        Assert.NotEqual(SelfCheckRecovery.NewToken(), SelfCheckRecovery.NewToken());
    }
}
