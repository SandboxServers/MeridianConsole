using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Leasing;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Leasing;

public sealed class LeaseBookTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Acquire_FreeBookWithoutTtl_GrantsDefaultTtl()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));

        Assert.Equal(600, grant.Info.TtlSeconds);
        Assert.Equal(Start.AddSeconds(600), grant.Info.ExpiresAt);
        Assert.Equal("s1", grant.Info.Owner);
        Assert.Equal("uat", grant.Info.Purpose);
    }

    [Fact]
    public void Acquire_WhileHeld_RefusesNamingHolderPurposeAndTimes()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        Result<LeaseGrant> refused = book.Acquire("s2", "soak", null, false, null);

        Assert.True(refused.IsFailure);
        Assert.Equal(
            "[Lab.LeaseHeld] lease held by s1 for \"uat\" since 2026-11-02T10:00:00Z, expires 2026-11-02T10:10:00Z",
            refused.Error);
    }

    [Fact]
    public void Acquire_TtlBelowMinimum_ClampsToThirty()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        LeaseGrant grant = Grant(book.Acquire("s1", "uat", 10, false, null));

        Assert.Equal(30, grant.Info.TtlSeconds);
    }

    [Fact]
    public void Acquire_TtlAboveMaximum_ClampsToOneHour()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        LeaseGrant grant = Grant(book.Acquire("s1", "uat", 9999, false, null));

        Assert.Equal(3600, grant.Info.TtlSeconds);
    }

    [Fact]
    public void Touch_WithLeaseId_ExtendsExpiryByTtl()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));
        time.Advance(TimeSpan.FromSeconds(300));

        Result<LeaseInfo> touched = book.Touch(grant.LeaseId);

        Assert.True(touched.IsSuccess);
        Assert.Equal(Start.AddSeconds(300 + 600), touched.Value.ExpiresAt);
    }

    [Fact]
    public void Sweep_AtExactlyExpiresAt_ExpiresLease()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", 30, false, null));

        time.Advance(TimeSpan.FromMilliseconds(29_999));
        Assert.Equal(0, book.Sweep());
        Assert.NotNull(book.Current);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, book.Sweep());
        Assert.Null(book.Current);
    }

    [Fact]
    public void Acquire_ForceWithoutReason_IsRefused()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        Result<LeaseGrant> refused = book.Acquire("s2", "soak", null, true, null);

        Assert.True(refused.IsFailure);
        Assert.Equal("[Lab.InvalidArgument] force needs a reason", refused.Error);
        Assert.Equal("s1", book.Current?.Owner);
    }

    [Fact]
    public void Acquire_ForcedTakeover_RecordsHistoryWithTakenOverBy()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        LeaseGrant takeover = Grant(book.Acquire("s2", "soak", null, true, "s1 wedged"));

        Assert.Equal("s2", book.Current?.Owner);
        Assert.Single(book.History);
        LeaseHistoryEntry entry = book.History[0];
        Assert.Equal("s1", entry.Owner);
        Assert.Equal(LeaseEndReason.TakenOver, entry.Reason);
        Assert.Equal("s2", entry.TakenOverBy);
        Assert.Equal("s1 wedged", entry.TakeoverReason);
        Assert.Equal("s2", takeover.Info.Owner);
    }

    [Fact]
    public void Release_WrongIdWhileHeld_ReturnsLeaseHeld()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        Result result = book.Release("not-the-lease");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.LeaseHeld]", result.Error, StringComparison.Ordinal);
        Assert.Equal("s1", book.Current?.Owner);
    }

    [Fact]
    public void Release_WrongIdWhileFree_ReturnsLeaseRequired()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        Result result = book.Release("not-the-lease");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.LeaseRequired]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Touch_WrongIdWhileFree_ReturnsLeaseRequired()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        Result<LeaseInfo> result = book.Touch("not-the-lease");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.LeaseRequired]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LeaseEnded_FiresOnRelease()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));
        List<LeaseHistoryEntry> ended = CaptureEnded(book);

        Assert.True(book.Release(grant.LeaseId).IsSuccess);

        LeaseHistoryEntry entry = Assert.Single(ended);
        Assert.Equal(LeaseEndReason.Released, entry.Reason);
    }

    [Fact]
    public void LeaseEnded_FiresOnExpiry()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", 30, false, null));
        List<LeaseHistoryEntry> ended = CaptureEnded(book);

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, book.Sweep());

        LeaseHistoryEntry entry = Assert.Single(ended);
        Assert.Equal(LeaseEndReason.Expired, entry.Reason);
        Assert.Equal(Start.AddSeconds(30), entry.EndedAt);
    }

    [Fact]
    public void LeaseEnded_FiresOnTakeover()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));
        List<LeaseHistoryEntry> ended = CaptureEnded(book);

        Grant(book.Acquire("s2", "soak", null, true, "operator override"));

        LeaseHistoryEntry entry = Assert.Single(ended);
        Assert.Equal(LeaseEndReason.TakenOver, entry.Reason);
        Assert.Equal("s1", entry.Owner);
    }

    [Fact]
    public void Acquire_Success_EmitsAcquiredLabLeaseEvent()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();

        Grant(book.Acquire("s1", "uat", null, false, null));

        LabEvent labEvent = Assert.Single(events.Items);
        Assert.Equal("lab.lease", labEvent.Name);
        Assert.Equal("acquired", labEvent.Fields["action"]);
        Assert.Equal("s1", labEvent.Fields["owner"]);
        Assert.Equal("uat", labEvent.Fields["purpose"]);
    }

    [Fact]
    public void Renew_Success_EmitsRenewedLabLeaseEvent()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));

        Assert.True(book.Renew(grant.LeaseId, 900).IsSuccess);

        LabEvent renewed = events.Items[^1];
        Assert.Equal("renewed", renewed.Fields["action"]);
        Assert.Equal("900", renewed.Fields["ttl_seconds"]);
    }

    [Fact]
    public void Release_Success_EmitsReleasedEvent()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));

        Assert.True(book.Release(grant.LeaseId).IsSuccess);

        LabEvent released = events.Items[^1];
        Assert.Equal("released", released.Fields["action"]);
        Assert.Equal("s1", released.Fields["owner"]);
    }

    [Fact]
    public void Sweep_Expiry_EmitsExpiredEvent()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents events) = Create();
        Grant(book.Acquire("s1", "uat", 30, false, null));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, book.Sweep());

        LabEvent expired = events.Items[^1];
        Assert.Equal("expired", expired.Fields["action"]);
        Assert.Equal("s1", expired.Fields["owner"]);
    }

    [Fact]
    public void Acquire_ForcedTakeover_EmitsTakenOverEventWithReason()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        Grant(book.Acquire("s2", "soak", null, true, "s1 wedged"));

        LabEvent takeover = Assert.Single(events.Items, e => e.Fields["action"] == "taken_over");
        Assert.Equal("s2", takeover.Fields["owner"]);
        Assert.Equal("s1", takeover.Fields["previous_owner"]);
        Assert.Equal("s1 wedged", takeover.Fields["reason"]);
    }

    [Fact]
    public void Acquire_WhileHeld_EmitsRefusedEvent()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));

        Assert.True(book.Acquire("s2", "soak", null, false, null).IsFailure);

        LabEvent refused = events.Items[^1];
        Assert.Equal("refused", refused.Fields["action"]);
        Assert.Equal("s1", refused.Fields["holder"]);
    }

    [Fact]
    public void Touch_AfterExpiryWithoutSweep_ReturnsLeaseRequired()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", 30, false, null));

        time.Advance(TimeSpan.FromSeconds(30));
        Result<LeaseInfo> touched = book.Touch(grant.LeaseId);

        Assert.True(touched.IsFailure);
        Assert.StartsWith("[Lab.LeaseRequired]", touched.Error, StringComparison.Ordinal);
        LeaseHistoryEntry entry = Assert.Single(book.History);
        Assert.Equal(LeaseEndReason.Expired, entry.Reason);
    }

    [Fact]
    public void Acquire_AfterExpiryWithoutSweep_GrantsNewLease()
    {
        (LeaseBook book, FakeTimeProvider time, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", 30, false, null));

        time.Advance(TimeSpan.FromSeconds(30));
        LeaseGrant next = Grant(book.Acquire("s2", "soak", null, false, null));

        Assert.Equal("s2", next.Info.Owner);
        Assert.Equal(LeaseEndReason.Expired, book.History[0].Reason);
    }

    [Fact]
    public void Renew_TtlAboveMaximum_ClampsToOneHour()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));

        Result<LeaseInfo> renewed = book.Renew(grant.LeaseId, 9999);

        Assert.True(renewed.IsSuccess);
        Assert.Equal(3600, renewed.Value.TtlSeconds);
        Assert.Equal(Start.AddSeconds(3600), renewed.Value.ExpiresAt);
    }

    [Fact]
    public void Events_NeverContainLeaseId()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents events) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));
        Assert.True(book.Renew(grant.LeaseId, null).IsSuccess);
        Assert.True(book.Release(grant.LeaseId).IsSuccess);
        LeaseGrant second = Grant(book.Acquire("s2", "soak", null, false, null));
        Result refused = book.Release("wrong-id-0123456789abcdef");

        Assert.True(refused.IsFailure);
        Assert.NotEmpty(events.Items);
        foreach (LabEvent labEvent in events.Items)
        {
            foreach (string? value in labEvent.Fields.Values)
            {
                Assert.DoesNotContain(grant.LeaseId, value ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(second.LeaseId, value ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("wrong-id-0123456789abcdef", value ?? string.Empty, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain(second.LeaseId, refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LeaseGrant_ToString_OmitsLeaseId()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));

        Assert.DoesNotContain(grant.LeaseId, grant.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(grant.LeaseId, Result<LeaseGrant>.Success(grant).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LeaseEnded_FirstHandlerThrows_SecondStillRuns()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        LeaseGrant grant = Grant(book.Acquire("s1", "uat", null, false, null));
        book.LeaseEnded += (_, _) => throw new InvalidOperationException("subscriber failure");
        List<LeaseHistoryEntry> ended = CaptureEnded(book);

        Assert.True(book.Release(grant.LeaseId).IsSuccess);

        Assert.Single(ended);
    }

    [Fact]
    public void ForcedTakeover_HandlerThrows_StillReturnsNewLeaseId()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();
        Grant(book.Acquire("s1", "uat", null, false, null));
        book.LeaseEnded += (_, _) => throw new InvalidOperationException("subscriber failure");

        LeaseGrant takeover = Grant(book.Acquire("s2", "soak", null, true, "s1 wedged"));

        Assert.Equal("s2", takeover.Info.Owner);
        Assert.True(book.Renew(takeover.LeaseId, null).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nowner")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("s1\u2028[Lab.LeaseHeld] forged")]
    [InlineData("s1\u202Eforged")]
    public void Acquire_InvalidOwner_IsRefusedAsInvalidArgument(string owner)
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        Result<LeaseGrant> result = book.Acquire(owner, "uat", null, false, null);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Acquire_OwnerOver100Characters_IsRefusedAsInvalidArgument()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        Result<LeaseGrant> result = book.Acquire(new string('x', 101), "uat", null, false, null);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Acquire_LoneSurrogateInOwner_IsRefusedAsInvalidArgument()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create();

        Result<LeaseGrant> result = book.Acquire("s1" + (char)0xD800, "uat", null, false, null);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void History_IsCappedAtHistorySize()
    {
        (LeaseBook book, FakeTimeProvider _, RecordingEvents _) = Create(new LeaseOptions(HistorySize: 2));

        for (int i = 0; i < 3; i++)
        {
            LeaseGrant grant = Grant(book.Acquire("s1", $"run-{i}", null, false, null));
            Assert.True(book.Release(grant.LeaseId).IsSuccess);
        }

        Assert.Equal(2, book.History.Count);
        Assert.Equal("run-1", book.History[0].Purpose);
        Assert.Equal("run-2", book.History[1].Purpose);
    }

    private static (LeaseBook Book, FakeTimeProvider Time, RecordingEvents Events) Create(LeaseOptions? options = null)
    {
        FakeTimeProvider time = new(Start);
        RecordingEvents events = new();
        LeaseBook book = new(time, events, options ?? new LeaseOptions());
        return (book, time, events);
    }

    private static List<LeaseHistoryEntry> CaptureEnded(LeaseBook book)
    {
        List<LeaseHistoryEntry> ended = [];
        book.LeaseEnded += (_, entry) => ended.Add(entry);
        return ended;
    }

    private static LeaseGrant Grant(Result<LeaseGrant> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private sealed class RecordingEvents : ILabEvents
    {
        public List<LabEvent> Items { get; } = [];

        public void Emit(LabEvent labEvent) => Items.Add(labEvent);
    }
}
