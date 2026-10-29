using Dhadgar.Lab.Chaos;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Chaos;

/// <summary>
/// The seeded chaos timeline (L29, R-SEED): the same seed gives the same timeline, and every entry stays inside the plan's ranges.
/// </summary>
public sealed class ChaosScheduleTests
{
    private const string Plan = """
        schema = 1
        [plan]
        id = "unit-plan"
        workload = { spec = "docs/guides/uat-specs/lab-selftest.toml", rows = ["enroll-gateway"] }
        max_concurrent = 2
        quiet_s = [5, 30]
        [[fault]]
        tool = "fault_toxic_add"
        args = { proxy = "nodes-postgres", toxic = "latency", stream = "downstream", attributes = { latency = 1500 } }
        weight = 3
        duration_s = [5, 45]
        [[fault]]
        tool = "fault_proxy_disable"
        args = { proxy = "gateway-nodes" }
        weight = 1
        duration_s = [10, 20]
        """;

    private const int RunSeconds = 600;

    [Fact]
    public void ChaosSchedule_SameSeed_SameTimeline()
    {
        ChaosPlan plan = LoadPlan();

        IReadOnlyList<ChaosScheduleEntry> first = plan.Schedule(42UL, RunSeconds);
        IReadOnlyList<ChaosScheduleEntry> second = plan.Schedule(42UL, RunSeconds);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ChaosSchedule_DifferentSeed_DifferentTimeline()
    {
        ChaosPlan plan = LoadPlan();

        Assert.NotEqual(plan.Schedule(1UL, RunSeconds), plan.Schedule(2UL, RunSeconds));
    }

    [Fact]
    public void ChaosSchedule_EntriesStayInsidePlanRanges()
    {
        ChaosPlan plan = LoadPlan();

        IReadOnlyList<ChaosScheduleEntry> timeline = plan.Schedule(7UL, RunSeconds);

        int previous = 0;
        foreach (ChaosScheduleEntry entry in timeline)
        {
            int gap = entry.StartS - previous;
            Assert.InRange(gap, plan.MinQuietS, plan.MaxQuietS);
            Assert.InRange(entry.StartS, 0, RunSeconds - 1);
            Assert.InRange(entry.FaultIndex, 0, plan.Faults.Count - 1);
            ChaosFault fault = plan.Faults[entry.FaultIndex];
            Assert.InRange(entry.DurationS, fault.MinDurationS, fault.MaxDurationS);
            previous = entry.StartS;
        }
    }

    [Fact]
    public void ChaosSchedule_WeightedPick_FavoursHeavierFault()
    {
        // Weight 3 against weight 1: the heavier fault is picked for roughly three in four entries over a long seeded run.
        ChaosPlan plan = LoadPlan();

        IReadOnlyList<ChaosScheduleEntry> timeline = plan.Schedule(99UL, 14400);

        int heavy = timeline.Count(entry => entry.FaultIndex == 0);
        double share = (double)heavy / timeline.Count;
        Assert.InRange(share, 0.6, 0.9);
    }

    private static ChaosPlan LoadPlan()
    {
        Result<ChaosPlan> loaded = ChaosPlanLoader.Load(Plan);
        Assert.True(loaded.IsSuccess, loaded.Error);
        return loaded.Value!;
    }
}
