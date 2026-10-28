using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Leasing;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The fault book: write-ahead journal, deadlines, recovery, lease-end healing and history.
/// </summary>
public sealed class FaultBookTests : IDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string LabContext = "lab-ctx";
    private const string TargetId = "c:gateway";
    private const string Kind = "pause";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fb-" + Guid.NewGuid().ToString("N"));
    private readonly LabPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly FaultJournal _journal;
    private readonly FakeKind _kind = new(Kind);

    public FaultBookTests()
    {
        _paths = LabPaths.FromRoot(_root);
        _journal = new FaultJournal(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task FaultBook_DeadlineHealsAtHealAfterS()
    {
        using var book = Book();
        await book.StartAsync(CancellationToken.None);
        Assert.True((await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None)).IsSuccess);

        _time.Advance(TimeSpan.FromSeconds(119));
        await Task.Delay(50);
        Assert.Single(book.ListActive());

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => book.ListActive().Count == 0);
        Assert.Equal(1, _kind.HealCalls);
        await book.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LeaseEnd_HealsAllFaults()
    {
        using var book = Book();
        Assert.True((await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None)).IsSuccess);

        var ended = new LeaseHistoryEntry("owner", "purpose", _time.GetUtcNow(), _time.GetUtcNow(), default, null, null);
        await book.OnLeaseEndedAsync(ended, CancellationToken.None);

        Assert.Empty(book.ListActive());
        Assert.Equal(1, _kind.HealCalls);
    }

    [Fact]
    public async Task FaultBook_RecoveryHealsJournalledEntry_AndRunsReconcile()
    {
        await _journal.WriteAsync([Entry("f-recover", FaultState.Active)], CancellationToken.None);
        using var book = Book();

        await book.StartAsync(CancellationToken.None);

        Assert.Equal("f-recover", Assert.Single(_kind.Healed));
        Assert.Equal(1, _kind.ReconcileCalls);
        Assert.Empty(book.ListActive());
        Assert.Empty((await _journal.ReadAsync(CancellationToken.None)).Value);
        await book.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FaultBook_SecondSameKindSameTarget_FaultActive()
    {
        using var book = Book();
        var first = await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.StartsWith("[Lab.FaultActive] " + first.Value.FaultId, second.Error, StringComparison.Ordinal);
        Assert.Equal(1, _kind.InjectCalls);
    }

    [Fact]
    public async Task FaultBook_HealErrorRetriedOnceThenReported()
    {
        using var book = Book();
        var fault = (await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None)).Value;
        _kind.HealError = "[Lab.FaultKind] still paused";

        var healed = await book.HealAsync(fault.FaultId, "operator", CancellationToken.None);

        Assert.True(healed.IsFailure);
        Assert.Equal(2, _kind.HealCalls);
        var active = Assert.Single(book.ListActive());
        Assert.Equal(FaultState.HealFailed, active.State);
        Assert.Equal("[Lab.FaultKind] still paused", active.HealError);
        Assert.True(book.IsFaulted(TargetId));
        Assert.Equal(fault.FaultId, Assert.Single((await _journal.ReadAsync(CancellationToken.None)).Value).FaultId);
    }

    [Fact]
    public async Task FaultBook_IsFaulted_WindowAfterHeal()
    {
        using var book = Book();
        var fault = (await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None)).Value;
        Assert.True(book.IsFaulted(TargetId));

        Assert.True((await book.HealAsync(fault.FaultId, "operator", CancellationToken.None)).IsSuccess);
        Assert.True(book.IsFaulted(TargetId));

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(book.IsFaulted(TargetId));
        Assert.False(book.IsFaulted("c:other"));
    }

    [Fact]
    public async Task FaultBook_JournalWrittenBeforeInject()
    {
        FaultState? seen = null;
        _kind.OnInject = _ =>
        {
            seen = _journal.ReadAsync(CancellationToken.None).GetAwaiter().GetResult().Value.SingleOrDefault()?.State;
        };
        using var book = Book();

        Assert.True((await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None)).IsSuccess);

        Assert.Equal(FaultState.Prepared, seen);
    }

    [Fact]
    public async Task FaultBook_CrashAfterInjectBeforeActive_RecoveryHeals()
    {
        using var book = Book(new ThrowOnActiveJournal(_journal));

        var begun = await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None);

        Assert.True(begun.IsFailure);
        Assert.Single(_kind.Injected);

        // A new book on the same state folder: recovery heals the entry that the journal still holds as Prepared.
        using var restarted = Book();
        await restarted.StartAsync(CancellationToken.None);

        Assert.Empty(_kind.Injected);
        Assert.Empty(restarted.ListActive());
        await restarted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FaultBook_PreparedNeverInjected_HealSucceeds()
    {
        await _journal.WriteAsync([Entry("f-prepared", FaultState.Prepared)], CancellationToken.None);
        using var book = Book();

        await book.StartAsync(CancellationToken.None);

        Assert.Equal("f-prepared", Assert.Single(_kind.Healed));
        Assert.Empty((await _journal.ReadAsync(CancellationToken.None)).Value);
        await book.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FaultBook_InjectFails_PartialHealed()
    {
        _kind.InjectError = "[Lab.FaultKind] inject failed halfway";
        using var book = Book();

        var begun = await book.BeginAsync(Kind, TargetId, Args("{}"), 120, CancellationToken.None);

        Assert.Equal("[Lab.FaultKind] inject failed halfway", begun.Error);
        Assert.Equal(1, _kind.HealCalls);
        Assert.Empty(book.ListActive());
        Assert.Empty((await _journal.ReadAsync(CancellationToken.None)).Value);
    }

    [Fact]
    public async Task FaultBook_HealAfterSOutOfRange_IsInvalidArgument()
    {
        using var book = Book();

        var result = await book.BeginAsync(Kind, TargetId, Args("{}"), 0, CancellationToken.None);

        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, _kind.PrepareCalls);
    }

    [Fact]
    public async Task FaultJournal_TempThenMove()
    {
        await _journal.WriteAsync([Entry("f-before", FaultState.Active)], CancellationToken.None);

        // Holding the journal open exclusively makes the move-over fail, as an interrupted write would.
        using (new FileStream(_journal.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var written = await _journal.WriteAsync([Entry("f-after", FaultState.Active)], CancellationToken.None);
            Assert.True(written.IsFailure);
        }

        var read = await _journal.ReadAsync(CancellationToken.None);
        Assert.Equal("f-before", Assert.Single(read.Value).FaultId);
        Assert.Empty(Directory.GetFiles(_paths.State, "*.tmp"));
    }

    private FaultBook Book(IFaultJournal? journal = null)
    {
        var registry = new FakeRegistry();
        registry.Register(TargetId, LabTargetKind.Container, "gateway", ContainerId);
        var guard = new FaultTargetGuard(registry, new FakeInspector(LabInspection(), LabContext), new PassingTripwire());
        return new FaultBook(guard, journal ?? _journal, [_kind], _time, NullLogger<FaultBook>.Instance);
    }

    private static FaultJournalEntry Entry(string faultId, FaultState state) =>
        new(faultId, Kind, TargetId, Args("{}"), Args("{\"prior\":\"running\"}"), state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(2), null);

    private static JsonElement Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("condition was not met in time");
    }

    private static ContainerInspection LabInspection() =>
        new(
            ContainerId,
            "/meridian-lab-gateway",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1" },
            [],
            0,
            DateTimeOffset.UnixEpoch,
            LabContext);

    private sealed class FakeRegistry : ILabTargetRegistry
    {
        private readonly Dictionary<string, LabTarget> _targets = new(StringComparer.Ordinal);

        public IReadOnlyList<LabTarget> All => _targets.Values.ToList();

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId)
        {
            var target = new LabTarget(targetId, kind, name, containerId, DateTimeOffset.UnixEpoch);
            _targets[targetId] = target;
            return Result<LabTarget>.Success(target);
        }

        public LabTarget? Find(string targetId) => _targets.GetValueOrDefault(targetId);

        public void Remove(string targetId) => _targets.Remove(targetId);

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class FakeInspector(ContainerInspection inspection, string labContext) : IDockerInspector
    {
        public string LabContext { get; } = labContext;

        public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct) =>
            Task.FromResult(Result<ContainerInspection>.Success(inspection));
    }

    private sealed class PassingTripwire : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Success();
    }

    /// <summary>
    /// Fails the journal write that records an entry as Active, as a crash between inject and that write would.
    /// </summary>
    private sealed class ThrowOnActiveJournal(FaultJournal inner) : IFaultJournal
    {
        public Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadAsync(CancellationToken ct) => inner.ReadAsync(ct);

        public Task<Result> WriteAsync(IReadOnlyList<FaultJournalEntry> entries, CancellationToken ct)
        {
            if (entries.Any(e => e.State == FaultState.Active))
            {
                throw new IOException("simulated crash before the Active write");
            }

            return inner.WriteAsync(entries, ct);
        }
    }

    /// <summary>
    /// A fault kind that records calls and can be told to fail. "Injected" tracks the simulated target.
    /// </summary>
    private sealed class FakeKind(string kind) : IFaultKind
    {
        public string Kind { get; } = kind;

        public string? InjectError { get; set; }

        public string? HealError { get; set; }

        public Action<string>? OnInject { get; set; }

        public int PrepareCalls { get; private set; }

        public int InjectCalls { get; private set; }

        public int HealCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public List<string> Healed { get; } = [];

        public HashSet<string> Injected { get; } = new(StringComparer.Ordinal);

        public Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
        {
            PrepareCalls++;
            return Task.FromResult(Result<JsonElement>.Success(Args("{\"prior\":\"running\"}")));
        }

        public Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
        {
            InjectCalls++;
            OnInject?.Invoke(faultId);
            if (InjectError is not null)
            {
                Injected.Add(faultId);
                return Task.FromResult(Result.Failure(InjectError));
            }

            Injected.Add(faultId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
        {
            HealCalls++;
            Healed.Add(entry.FaultId);
            if (HealError is not null)
            {
                return Task.FromResult(Result.Failure(HealError));
            }

            Injected.Remove(entry.FaultId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<int>> ReconcileAsync(CancellationToken ct)
        {
            ReconcileCalls++;
            return Task.FromResult(Result<int>.Success(0));
        }
    }
}
