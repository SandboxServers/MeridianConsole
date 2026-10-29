using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Soak;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I7 over fakes: a flat soak is clean, a rising series over the limit violates, and the three outcomes hold (a violation,
/// a skip outside a soak or below three samples, and a sampler failure as a check error).
/// </summary>
public sealed class I7Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly Func<Result<SoakLimits>> Limits = () => Result<SoakLimits>.Success(new SoakLimits(50, 10));

    [Fact]
    public async Task I7_FlatSeries_NoViolation()
    {
        var store = new ResourceSampleStore();
        store.Begin();
        for (int i = 0; i < 6; i++)
        {
            store.Add(Sample(i * 30, memory: 100, pids: 12));
        }

        Result<InvariantOutcome> outcome = await new I7ResourceTrendInvariant(store, Limits).CheckWithSkipsAsync(Window(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Empty(outcome.Value!.Violations);
        Assert.Empty(outcome.Value.Skipped);
    }

    [Fact]
    public async Task I7_ThreeSamplesRising_ProducesViolation()
    {
        // 100 MiB per hour of growth, over a 50 MiB per hour limit.
        var store = new ResourceSampleStore();
        store.Begin();
        store.Add(Sample(0, memory: 100, pids: 12));
        store.Add(Sample(1800, memory: 150, pids: 12));
        store.Add(Sample(3600, memory: 200, pids: 12));

        Result<IReadOnlyList<InvariantViolation>> result = await new I7ResourceTrendInvariant(store, Limits).CheckAsync(Window(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        InvariantViolation violation = Assert.Single(result.Value!);
        Assert.Equal("I7", violation.InvariantId);
        Assert.Equal("trend", violation.CheckId);
        Assert.Equal("ml-a", violation.Service);
    }

    [Fact]
    public async Task I7_RisingPids_ProducesViolation()
    {
        var store = new ResourceSampleStore();
        store.Begin();
        store.Add(Sample(0, memory: 100, pids: 10));
        store.Add(Sample(1800, memory: 100, pids: 20));
        store.Add(Sample(3600, memory: 100, pids: 30));

        Result<InvariantOutcome> outcome = await new I7ResourceTrendInvariant(store, Limits).CheckWithSkipsAsync(Window(), CancellationToken.None);

        Assert.Single(outcome.Value!.Violations);
        Assert.Contains("pids", outcome.Value.Violations[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I7_NoSoakRunning_Skips()
    {
        var store = new ResourceSampleStore();

        Result<InvariantOutcome> outcome = await new I7ResourceTrendInvariant(store, Limits).CheckWithSkipsAsync(Window(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Empty(outcome.Value!.Violations);
        InvariantSkip skip = Assert.Single(outcome.Value.Skipped);
        Assert.Equal("I7", skip.InvariantId);
        Assert.Equal("soak", skip.Source);
    }

    [Fact]
    public async Task I7_TwoSamples_Skips()
    {
        var store = new ResourceSampleStore();
        store.Begin();
        store.Add(Sample(0, memory: 100, pids: 10));
        store.Add(Sample(1800, memory: 900, pids: 90));

        Result<InvariantOutcome> outcome = await new I7ResourceTrendInvariant(store, Limits).CheckWithSkipsAsync(Window(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Empty(outcome.Value!.Violations);
        Assert.Single(outcome.Value.Skipped);
    }

    [Fact]
    public async Task I7_SamplerError_IsCheckError()
    {
        var store = new ResourceSampleStore();
        store.Begin();
        store.Add(Sample(0, memory: 100, pids: 10));
        store.Fail("[Lab.Docker] docker stats failed");

        var invariant = new I7ResourceTrendInvariant(store, Limits);

        Result<InvariantOutcome> outcome = await invariant.CheckWithSkipsAsync(Window(), CancellationToken.None);
        Result<IReadOnlyList<InvariantViolation>> plain = await invariant.CheckAsync(Window(), CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.True(plain.IsFailure);
    }

    [Fact]
    public async Task I7_UnreadableLimits_IsCheckError()
    {
        var store = new ResourceSampleStore();
        store.Begin();
        store.Add(Sample(0, memory: 100, pids: 10));
        store.Add(Sample(1800, memory: 100, pids: 10));
        store.Add(Sample(3600, memory: 100, pids: 10));

        Result<InvariantOutcome> outcome = await new I7ResourceTrendInvariant(store, () => Result<SoakLimits>.Failure("[Lab.NotFound] missing"))
            .CheckWithSkipsAsync(Window(), CancellationToken.None);

        Assert.True(outcome.IsFailure);
    }

    [Fact]
    public void SoakLimits_Load_ReadsTheShippedFile()
    {
        string path = Path.Combine(FindRepoRoot(), "docs", "guides", "uat-specs", "invariants", "soak.toml");

        Result<SoakLimits> limits = SoakLimits.Load(path);

        Assert.True(limits.IsSuccess);
        Assert.Equal(50d, limits.Value!.MemoryMibPerHour);
        Assert.Equal(10d, limits.Value.PidsPerHour);
    }

    [Fact]
    public void SoakLimits_Load_MissingKey_IsFailure()
    {
        string dir = Path.Combine(Path.GetTempPath(), "i7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "soak.toml");
            File.WriteAllText(path, "schema = 1\n[limits]\nmemory_mib_per_hour = 50\n");

            Assert.True(SoakLimits.Load(path).IsFailure);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static InvariantWindow Window() => new(T0, T0 + TimeSpan.FromHours(1), null, null);

    private static ResourceSampleSet Sample(int seconds, double memory, double pids) =>
        new(T0 + TimeSpan.FromSeconds(seconds), new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["memory_mib:ml-a"] = memory,
            ["pids:ml-a"] = pids,
        });

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Packages.props")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("repository root not found");
    }
}
