using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// I4: no secret in a log (C11). The observer sees every raw child line before <see cref="Redactor"/> runs. A line that holds a
/// PEM private key, a <c>Bearer</c> token, a non-empty <c>Password=</c>/<c>Pwd=</c> value, a vault value or the daemon token is a
/// violation. The raw line is matched and dropped; only the redacted line is queued and reported.
/// </summary>
public sealed class I4SecretLeakInvariant : IInvariant, IRawLineObserver
{
    private const int MinSecretLength = 8;
    private const int MaxPending = 1000;
    private const int MaxDetailLength = 512;
    private const int EvidenceLineLength = 512;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex PemKey = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex BearerToken = new(
        @"Bearer\s+[A-Za-z0-9._~+/=-]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private static readonly Regex PasswordValue = new(
        @"(Password|Pwd)=[^;\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    private readonly SecretVault _vault;
    private readonly Redactor _redactor;
    private readonly Func<string, bool>? _matchesDaemonToken;
    private readonly TimeProvider _time;
    private readonly ConcurrentQueue<InvariantViolation> _pending = new();
    private int _pendingCount;
    private int _dropped;

    /// <summary>
    /// Creates the check. <paramref name="matchesDaemonToken"/> answers whether a raw line holds the daemon token; it is null when no token is wired.
    /// </summary>
    public I4SecretLeakInvariant(SecretVault vault, Redactor redactor, Func<string, bool>? matchesDaemonToken = null, TimeProvider? time = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _matchesDaemonToken = matchesDaemonToken;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Id => "I4";

    /// <summary>Lines that matched but could not be queued because the queue was full.</summary>
    public int DroppedCount => Volatile.Read(ref _dropped);

    /// <inheritdoc />
    public void Observe(string process, ReadOnlySpan<char> rawLine)
    {
        if (rawLine.IsEmpty)
        {
            return;
        }

        // The raw text lives only in this frame; it is matched and then dropped, never logged or stored.
        string line = rawLine.ToString();
        string? checkId = FindLeak(line);
        if (checkId is null)
        {
            return;
        }

        if (Interlocked.Increment(ref _pendingCount) > MaxPending)
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _dropped);
            return;
        }

        string redacted = _redactor.Redact(line);
        string shown = redacted.Length > EvidenceLineLength ? redacted[..EvidenceLineLength] : redacted;
        string detail = $"{checkId} found in {process} output";
        var evidence = JsonSerializer.SerializeToElement(new { line = shown });

        _pending.Enqueue(new InvariantViolation(
            Id,
            process,
            checkId,
            detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail,
            _time.GetUtcNow(),
            evidence));
    }

    /// <summary>
    /// Returns every violation queued since the last checkpoint and clears the queue. Never fails.
    /// </summary>
    public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var drained = new List<InvariantViolation>();
        while (_pending.TryDequeue(out InvariantViolation? violation))
        {
            Interlocked.Decrement(ref _pendingCount);
            drained.Add(violation);
        }

        return Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Success(drained));
    }

    /// <summary>
    /// Returns the check id of the first secret pattern the raw line matches, or null when the line is clean.
    /// </summary>
    private string? FindLeak(string line)
    {
        if (PemKey.IsMatch(line))
        {
            return "pem-private-key";
        }

        if (BearerToken.IsMatch(line))
        {
            return "bearer-token";
        }

        if (PasswordValue.IsMatch(line))
        {
            return "password-value";
        }

        if (_matchesDaemonToken is not null && _matchesDaemonToken(line))
        {
            return "daemon-token";
        }

        foreach (var (reference, length) in _vault.Snapshot())
        {
            if (length < MinSecretLength)
            {
                continue;
            }

            Result<bool> hit = _vault.Use(reference, secret => line.AsSpan().IndexOf(secret, StringComparison.Ordinal) >= 0);
            if (hit.IsSuccess && hit.Value)
            {
                return $"vault-value:{reference}";
            }
        }

        return null;
    }
}
