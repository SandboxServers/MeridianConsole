using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Lab.Audit;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Leasing;

/// <summary>
/// Lease tuning. Defaults: TTL 600 s, bounds 30 to 3600 s, 50 ended leases kept in history.
/// </summary>
public sealed record LeaseOptions(int DefaultTtlSeconds = 600, int MinTtlSeconds = 30, int MaxTtlSeconds = 3600, int HistorySize = 50);

/// <summary>
/// The live lease as reported to callers. Never carries the lease id.
/// </summary>
public sealed record LeaseInfo(string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset ExpiresAt, int TtlSeconds);

/// <summary>
/// Returned by <see cref="LeaseBook.Acquire"/>: the one-time lease id (the caller's proof of ownership) plus the lease info.
/// <c>ToString</c> omits <c>LeaseId</c>.
/// </summary>
public sealed record LeaseGrant(string LeaseId, LeaseInfo Info)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Info = ").Append(Info);
        return true;
    }
}

/// <summary>
/// Why a lease ended.
/// </summary>
public enum LeaseEndReason { Released, Expired, TakenOver }

/// <summary>
/// One ended lease. <c>TakenOverBy</c> and <c>TakeoverReason</c> are set only for <see cref="LeaseEndReason.TakenOver"/>.
/// </summary>
public sealed record LeaseHistoryEntry(string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset EndedAt,
    LeaseEndReason Reason, string? TakenOverBy, string? TakeoverReason);

/// <summary>
/// The single lab lease: who holds the stack, for what purpose, and until when (C3). Thread-safe.
/// Every change emits one <c>lab.lease</c> event. Lease ids never appear in events, history or messages.
/// Expiry is checked against the injected clock on every call, so an overdue lease is never reported as held.
/// </summary>
public sealed class LeaseBook
{
    private const string LeaseEventName = "lab.lease";
    private const int MaxLabelLength = 100;
    private const int MaxReasonLength = 200;
    private const int LeaseIdBytes = 16;

    private readonly TimeProvider _time;
    private readonly ILabEvents _events;
    private readonly LeaseOptions _options;
    private readonly Lock _gate = new();
    private readonly List<LeaseHistoryEntry> _history = [];
    private ActiveLease? _current;

    /// <summary>
    /// Creates an empty lease book.
    /// </summary>
    /// <exception cref="ArgumentException">The TTL bounds or history size are inconsistent (a programmer error, not input).</exception>
    public LeaseBook(TimeProvider time, ILabEvents events, LeaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MinTtlSeconds < 1
            || options.MinTtlSeconds > options.DefaultTtlSeconds
            || options.DefaultTtlSeconds > options.MaxTtlSeconds)
        {
            throw new ArgumentException("TTL bounds must satisfy 1 <= Min <= Default <= Max.", nameof(options));
        }

        if (options.HistorySize < 0)
        {
            throw new ArgumentException("HistorySize must not be negative.", nameof(options));
        }

        _time = time;
        _events = events;
        _options = options;
    }

    /// <summary>
    /// Takes the lease. While it is held, only <paramref name="force"/> with a <paramref name="reason"/> takes it over,
    /// and the previous holder is recorded in history as <see cref="LeaseEndReason.TakenOver"/>.
    /// </summary>
    public Result<LeaseGrant> Acquire(string owner, string purpose, int? ttlSeconds, bool force, string? reason)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(purpose);

        if (!IsValidText(owner, MaxLabelLength))
        {
            return Result<LeaseGrant>.Failure("[Lab.InvalidArgument] owner must be 1 to 100 characters without control characters");
        }

        if (!IsValidText(purpose, MaxLabelLength))
        {
            return Result<LeaseGrant>.Failure("[Lab.InvalidArgument] purpose must be 1 to 100 characters without control characters");
        }

        if (force)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result<LeaseGrant>.Failure("[Lab.InvalidArgument] force needs a reason");
            }

            if (!IsValidText(reason, MaxReasonLength))
            {
                return Result<LeaseGrant>.Failure("[Lab.InvalidArgument] reason must be 1 to 200 characters without control characters");
            }
        }

        LeaseHistoryEntry? ended = null;
        Result<LeaseGrant> result;

        lock (_gate)
        {
            ended = ExpireIfDueLocked();
            DateTimeOffset now = _time.GetUtcNow();

            if (_current is null)
            {
                LeaseGrant grant = GrantLocked(owner, purpose, ClampTtl(ttlSeconds), now);
                EmitLocked("acquired", owner, purpose, "acquired", grant.Info);
                result = Result<LeaseGrant>.Success(grant);
            }
            else if (!force)
            {
                EmitLocked("refused", owner, purpose, "held", holder: _current.Info.Owner);
                result = Result<LeaseGrant>.Failure(HeldMessage(_current.Info));
            }
            else
            {
                ActiveLease previous = _current;
                LeaseHistoryEntry takeover = new(
                    previous.Info.Owner,
                    previous.Info.Purpose,
                    previous.Info.Since,
                    now,
                    LeaseEndReason.TakenOver,
                    owner,
                    reason);

                ended = takeover;
                RetireLocked(takeover);
                EmitLocked("taken_over", owner, purpose, reason!, previousOwner: previous.Info.Owner);

                LeaseGrant grant = GrantLocked(owner, purpose, ClampTtl(ttlSeconds), now);
                result = Result<LeaseGrant>.Success(grant);
            }
        }

        RaiseLeaseEnded(ended);
        return result;
    }

    /// <summary>
    /// Extends the lease by <paramref name="ttlSeconds"/> (clamped), or by its current TTL when null.
    /// </summary>
    public Result<LeaseInfo> Renew(string leaseId, int? ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(leaseId);

        LeaseHistoryEntry? ended;
        Result<LeaseInfo> result;

        lock (_gate)
        {
            ended = ExpireIfDueLocked();
            Result<ActiveLease> guard = GuardLocked(leaseId);

            if (guard.IsFailure)
            {
                result = Result<LeaseInfo>.Failure(guard.Error);
            }
            else
            {
                ActiveLease lease = guard.Value;
                int ttl = ttlSeconds is null ? lease.Info.TtlSeconds : ClampTtl(ttlSeconds);
                DateTimeOffset now = _time.GetUtcNow();
                lease.Info = lease.Info with { TtlSeconds = ttl, ExpiresAt = now.AddSeconds(ttl) };

                EmitLocked("renewed", lease.Info.Owner, lease.Info.Purpose, "renewed", lease.Info);
                result = Result<LeaseInfo>.Success(lease.Info);
            }
        }

        RaiseLeaseEnded(ended);
        return result;
    }

    /// <summary>
    /// Ends the lease at the caller's request.
    /// </summary>
    public Result Release(string leaseId)
    {
        ArgumentNullException.ThrowIfNull(leaseId);

        LeaseHistoryEntry? ended;
        Result result;

        lock (_gate)
        {
            ended = ExpireIfDueLocked();
            Result<ActiveLease> guard = GuardLocked(leaseId);

            if (guard.IsFailure)
            {
                result = Result.Failure(guard.Error);
            }
            else
            {
                ActiveLease lease = guard.Value;
                LeaseHistoryEntry entry = new(
                    lease.Info.Owner,
                    lease.Info.Purpose,
                    lease.Info.Since,
                    _time.GetUtcNow(),
                    LeaseEndReason.Released,
                    null,
                    null);

                ended = entry;
                RetireLocked(entry);
                EmitLocked("released", entry.Owner, entry.Purpose, "released");
                result = Result.Success();
            }
        }

        RaiseLeaseEnded(ended);
        return result;
    }

    /// <summary>
    /// Every guarded call: checks the lease id and extends <c>ExpiresAt</c> by the lease's TTL. Emits no event.
    /// </summary>
    public Result<LeaseInfo> Touch(string leaseId)
    {
        ArgumentNullException.ThrowIfNull(leaseId);

        LeaseHistoryEntry? ended;
        Result<LeaseInfo> result;

        lock (_gate)
        {
            ended = ExpireIfDueLocked();
            Result<ActiveLease> guard = GuardLocked(leaseId);

            if (guard.IsFailure)
            {
                result = Result<LeaseInfo>.Failure(guard.Error);
            }
            else
            {
                ActiveLease lease = guard.Value;
                lease.Info = lease.Info with { ExpiresAt = _time.GetUtcNow().AddSeconds(lease.Info.TtlSeconds) };
                result = Result<LeaseInfo>.Success(lease.Info);
            }
        }

        RaiseLeaseEnded(ended);
        return result;
    }

    /// <summary>
    /// The current lease, or null when free. An overdue lease is expired first and reported as free.
    /// </summary>
    public LeaseInfo? Current
    {
        get
        {
            LeaseHistoryEntry? ended;
            LeaseInfo? info;

            lock (_gate)
            {
                ended = ExpireIfDueLocked();
                info = _current?.Info;
            }

            RaiseLeaseEnded(ended);
            return info;
        }
    }

    /// <summary>
    /// Ended leases, oldest first, capped at <see cref="LeaseOptions.HistorySize"/>.
    /// </summary>
    public IReadOnlyList<LeaseHistoryEntry> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    /// <summary>
    /// Expires the lease when it is overdue. Called every 5 s by <see cref="LeaseSweeper"/>. Returns 0 or 1.
    /// </summary>
    public int Sweep()
    {
        LeaseHistoryEntry? ended;

        lock (_gate)
        {
            ended = ExpireIfDueLocked();
        }

        RaiseLeaseEnded(ended);
        return ended is null ? 0 : 1;
    }

    /// <summary>
    /// Raised once per lease that ends: released, expired or taken over. Raised outside the lock.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "C3 fixes the event as EventHandler<LeaseHistoryEntry>. LeaseHistoryEntry is a record value, not EventArgs; a record cannot derive from EventArgs.")]
    public event EventHandler<LeaseHistoryEntry>? LeaseEnded;

    private LeaseHistoryEntry? ExpireIfDueLocked()
    {
        if (_current is not { } lease || _time.GetUtcNow() < lease.Info.ExpiresAt)
        {
            return null;
        }

        LeaseHistoryEntry entry = new(
            lease.Info.Owner,
            lease.Info.Purpose,
            lease.Info.Since,
            lease.Info.ExpiresAt,
            LeaseEndReason.Expired,
            null,
            null);

        RetireLocked(entry);
        EmitLocked("expired", entry.Owner, entry.Purpose, "ttl_elapsed");
        return entry;
    }

    private Result<ActiveLease> GuardLocked(string leaseId)
    {
        if (_current is not { } lease)
        {
            EmitLocked("refused", null, null, "no_lease");
            return Result<ActiveLease>.Failure("[Lab.LeaseRequired] no lease is held; acquire one first");
        }

        if (!MatchesLocked(lease, leaseId))
        {
            EmitLocked("refused", null, null, "wrong_lease_id", holder: lease.Info.Owner);
            return Result<ActiveLease>.Failure(HeldMessage(lease.Info));
        }

        return Result<ActiveLease>.Success(lease);
    }

    private LeaseGrant GrantLocked(string owner, string purpose, int ttl, DateTimeOffset now)
    {
        string leaseId = NewLeaseId();
        byte[] idUtf8 = Encoding.UTF8.GetBytes(leaseId);
        LeaseInfo info = new(owner, purpose, now, now.AddSeconds(ttl), ttl);
        _current = new ActiveLease(idUtf8, info);
        return new LeaseGrant(leaseId, info);
    }

    private void RetireLocked(LeaseHistoryEntry entry)
    {
        _current?.Clear();
        _current = null;
        _history.Add(entry);

        int excess = _history.Count - _options.HistorySize;
        if (excess > 0)
        {
            _history.RemoveRange(0, excess);
        }
    }

    private void EmitLocked(
        string action,
        string? owner,
        string? purpose,
        string reason,
        LeaseInfo? info = null,
        string? holder = null,
        string? previousOwner = null)
    {
        Dictionary<string, string?> fields = new(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["owner"] = owner,
            ["purpose"] = purpose,
            ["reason"] = reason,
        };

        if (info is not null)
        {
            fields["ttl_seconds"] = info.TtlSeconds.ToString(CultureInfo.InvariantCulture);
            fields["expires_at"] = FormatInstant(info.ExpiresAt);
        }

        if (holder is not null)
        {
            fields["holder"] = holder;
        }

        if (previousOwner is not null)
        {
            fields["previous_owner"] = previousOwner;
        }

        _events.Emit(new LabEvent(LeaseEventName, _time.GetUtcNow(), fields));
    }

    /// <summary>
    /// Invokes each subscriber on its own, so a throwing subscriber neither escapes the public call (a forced takeover
    /// has already granted the new lease) nor stops later subscribers. Only the exception type is kept; messages are not.
    /// </summary>
    private void RaiseLeaseEnded(LeaseHistoryEntry? ended)
    {
        if (ended is null)
        {
            return;
        }

        Delegate[] handlers = LeaseEnded?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<LeaseHistoryEntry>)handler)(this, ended);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Contained: the lease change has already been committed, so the subscriber cannot undo it.
                // No message or type is emitted here; a subscriber that needs to report failures does so itself.
            }
        }
    }

    private int ClampTtl(int? requested) =>
        Math.Clamp(requested ?? _options.DefaultTtlSeconds, _options.MinTtlSeconds, _options.MaxTtlSeconds);

    private static bool MatchesLocked(ActiveLease lease, string leaseId)
    {
        byte[] candidate = Encoding.UTF8.GetBytes(leaseId);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, lease.IdUtf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private static string NewLeaseId()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(LeaseIdBytes);
        try
        {
            return Convert.ToHexStringLower(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    private static string HeldMessage(LeaseInfo info) =>
        $"[Lab.LeaseHeld] lease held by {info.Owner} for \"{info.Purpose}\" since {FormatInstant(info.Since)}, expires {FormatInstant(info.ExpiresAt)}";

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Text that is echoed into messages, events and history: 1 to <paramref name="maxLength"/> characters, not blank,
    /// with no control, format (bidi overrides, zero-width) or line/paragraph separator characters and no unpaired surrogate.
    /// </summary>
    private static bool IsValidText(string value, int maxLength)
    {
        if (value.Length < 1 || value.Length > maxLength || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(c, value[i + 1]));
                if (pairCategory is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                {
                    return false;
                }

                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                return false;
            }

            if (char.IsControl(c))
            {
                return false;
            }

            UnicodeCategory category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The live lease. Its id is kept only as UTF-8 bytes and zeroed when the lease ends.
    /// </summary>
    private sealed class ActiveLease
    {
        public ActiveLease(byte[] idUtf8, LeaseInfo info)
        {
            IdUtf8 = idUtf8;
            Info = info;
        }

        public byte[] IdUtf8 { get; }

        public LeaseInfo Info { get; set; }

        public void Clear() => CryptographicOperations.ZeroMemory(IdUtf8);
    }
}
