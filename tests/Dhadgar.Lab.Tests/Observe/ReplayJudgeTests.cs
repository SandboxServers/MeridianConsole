using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// The replay judge lists each changed fingerprint field and decides reproduction by fidelity.
/// </summary>
public sealed class ReplayJudgeTests
{
    [Fact]
    public void Diff_ListsChangedImageDigest()
    {
        LabFingerprint stored = Fingerprint(new Dictionary<string, string> { ["gateway"] = "sha-old" });
        LabFingerprint current = Fingerprint(new Dictionary<string, string> { ["gateway"] = "sha-new" });

        IReadOnlyList<string> diffs = ReplayJudge.Diff(stored, current);

        string diff = Assert.Single(diffs);
        Assert.StartsWith("image_digests.gateway: sha-old -> sha-new", diff);
    }

    [Fact]
    public void Diff_IdenticalFingerprints_IsEmpty()
    {
        LabFingerprint stored = Fingerprint(new Dictionary<string, string> { ["gateway"] = "sha-a" });

        Assert.Empty(ReplayJudge.Diff(stored, stored));
    }

    [Fact]
    public void Diff_PathLikeValues_AreHashed()
    {
        LabFingerprint stored = Fingerprint(new Dictionary<string, string>()) with { BuildId = "C:\\builds\\one" };
        LabFingerprint current = Fingerprint(new Dictionary<string, string>()) with { BuildId = "C:\\builds\\two" };

        string diff = Assert.Single(ReplayJudge.Diff(stored, current));
        Assert.DoesNotContain("C:\\builds", diff);
        Assert.Contains("sha256:", diff);
    }

    [Fact]
    public void Reproduced_ExactSameGrades_True()
    {
        bool? result = ReplayJudge.Reproduced(ReplayFidelity.Exact, ["pass", "fail"], [["pass", "fail"]]);

        Assert.True(result);
    }

    [Fact]
    public void Reproduced_ScheduleApproximateOnLaterAttempt_True()
    {
        bool? result = ReplayJudge.Reproduced(ReplayFidelity.ScheduleApproximate, ["fail"],
            [["pass"], ["pass"], ["fail"]]);

        Assert.True(result);
    }

    [Fact]
    public void Reproduced_ScheduleApproximateAfterFiveAttempts_False()
    {
        bool? result = ReplayJudge.Reproduced(ReplayFidelity.ScheduleApproximate, ["fail"],
            [["pass"], ["pass"], ["pass"], ["pass"], ["pass"], ["fail"]]);

        Assert.False(result);
    }

    [Fact]
    public void Reproduced_EvidenceOnly_Null()
    {
        bool? result = ReplayJudge.Reproduced(ReplayFidelity.EvidenceOnly, ["pass"], [["pass"]]);

        Assert.Null(result);
    }

    private static LabFingerprint Fingerprint(IReadOnlyDictionary<string, string> digests) => new(
        Schema: 1,
        Commit: "abc123",
        Dirty: false,
        BuildId: "build-1",
        Baseline: null,
        Mutant: null,
        ImageDigests: digests,
        LockFilesSha256: "lock",
        EffectiveConfigSha256: new Dictionary<string, string>(),
        Specs: [],
        DaemonVersion: "1.0.0",
        ToxiproxyVersion: "2.9.0",
        Seed: 7,
        Os: "windows",
        EngineVersions: new Dictionary<string, string>());
}
