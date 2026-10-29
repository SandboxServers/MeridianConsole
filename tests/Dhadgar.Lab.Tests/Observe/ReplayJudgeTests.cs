using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Results;

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

    [Fact]
    public void Judge_Exact_SameGradesDifferentPrimarySignature_IsNotReproduced()
    {
        ReplayVerdict verdict = ReplayJudge.Judge(ReplayFidelity.Exact,
            [new ReplayRow("fail", "clause:a|b")], [[new ReplayRow("fail", "clause:a|c")]]);

        Assert.False(verdict.Reproduced);
    }

    [Fact]
    public void Judge_Exact_SameGradesAndSignature_IsReproduced()
    {
        ReplayVerdict verdict = ReplayJudge.Judge(ReplayFidelity.Exact,
            [new ReplayRow("fail", "clause:a|b"), new ReplayRow("pass", null)],
            [[new ReplayRow("fail", "clause:a|b"), new ReplayRow("pass", null)]]);

        Assert.True(verdict.Reproduced);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void Judge_ScheduleApproximate_SignatureAppearsInLaterAttempt_IsReproduced()
    {
        ReplayVerdict verdict = ReplayJudge.Judge(ReplayFidelity.ScheduleApproximate,
            [new ReplayRow("fail", "inv:I1|nodes|unhandled")],
            [[new ReplayRow("fail", "clause:x|y")], [new ReplayRow("fail", "inv:I1|nodes|unhandled")]]);

        Assert.True(verdict.Reproduced);
    }

    [Fact]
    public void Judge_UnbuiltKindOriginal_ComparesBuiltKindsOnlyAndSaysSo()
    {
        ReplayVerdict verdict = ReplayJudge.Judge(ReplayFidelity.Exact,
            [new ReplayRow("fail", EvidenceCoverage.NotBuiltKinds[0] + ":500|-|POST|/a|none")], [[new ReplayRow("fail", null)]]);

        Assert.True(verdict.Reproduced);
        Assert.Contains("built signature kinds only", verdict.Reason);
    }

    [Fact]
    public void Judge_EvidenceOnly_IsNull()
    {
        Assert.Null(ReplayJudge.Judge(ReplayFidelity.EvidenceOnly, [], []).Reproduced);
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
