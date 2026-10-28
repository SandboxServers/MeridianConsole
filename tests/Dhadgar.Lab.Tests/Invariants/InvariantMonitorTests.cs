using Dhadgar.Lab.Invariants;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// The monitor runs every check: one that throws or fails becomes a check error and the others still run (C11).
/// </summary>
public sealed class InvariantMonitorTests
{
    private static readonly InvariantWindow Window = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null, null);

    [Fact]
    public async Task Monitor_OneCheckThrowsOrFails_OthersStillRun()
    {
        var monitor = new InvariantMonitor(
        [
            new FakeInvariant("I1", Throws: true),
            new FakeInvariant("I2", Fails: "source down"),
            new FakeInvariant("I3", Violations: 1),
        ]);

        InvariantReport report = await monitor.CheckpointAsync(Window, CancellationToken.None);

        Assert.Single(report.Violations);
        Assert.Equal("I3", report.Violations[0].InvariantId);
        Assert.Equal(2, report.Errors.Count);
        Assert.Contains(report.Errors, error => error.InvariantId == "I1" && error.Error == "check threw InvalidOperationException");
        Assert.Contains(report.Errors, error => error.InvariantId == "I2" && error.Error == "source down");
    }

    [Fact]
    public async Task Monitor_SkippableInvariant_SkipsAreStampedWithTheRow_AndAreNotErrors()
    {
        var monitor = new InvariantMonitor([new SkippingFake("I2")]);

        InvariantReport report = await monitor.CheckpointAsync(new InvariantWindow(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null, "r1"), CancellationToken.None);

        Assert.Empty(report.Errors);
        InvariantSkip skip = Assert.Single(report.Skipped!);
        Assert.Equal("r1", skip.Row);
    }

    private sealed class SkippingFake(string id) : ISkippableInvariant
    {
        public string Id { get; } = id;

        public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Success(Array.Empty<InvariantViolation>()));

        public Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct) =>
            Task.FromResult(Result<InvariantOutcome>.Success(new InvariantOutcome(
                Array.Empty<InvariantViolation>(),
                [new InvariantSkip(Id, "db:nodes", "stack not up (no lab credentials)")])));
    }

    private sealed class FakeInvariant(string id, bool Throws = false, string? Fails = null, int Violations = 0) : IInvariant
    {
        public string Id { get; } = id;

        public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
        {
            if (Throws)
            {
                throw new InvalidOperationException("secret=shh");
            }

            if (Fails is not null)
            {
                return Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Failure(Fails));
            }

            var found = new List<InvariantViolation>();
            for (int i = 0; i < Violations; i++)
            {
                found.Add(new InvariantViolation(Id, "lab", "fake", "fake", window.To, null));
            }

            return Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Success(found));
        }
    }
}
