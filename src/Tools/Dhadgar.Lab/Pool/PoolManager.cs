using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Leasing;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// The pool snapshot <c>pool_list</c> reports: each provider's availability and the current members.
/// </summary>
/// <param name="Providers">One entry per provider, in registration order.</param>
/// <param name="Members">Every member the pool holds.</param>
public sealed record PoolListing(IReadOnlyList<PoolProviderStatus> Providers, IReadOnlyList<PoolMember> Members);

/// <summary>
/// The machine pool (L30). Chooses the first provider whose dimensions match and that is available, enforces the per-OS limit,
/// and registers each member as <c>m:&lt;id&gt;</c> before its container starts (R-BLAST). Destroys members only through the
/// <see cref="FaultTargetGuard"/>. Lease end destroys every container member; startup destroys whatever <c>pool.json</c> lists.
/// </summary>
public sealed class PoolManager : ILeaseEndHook, ILabStatusSource, IHostedService
{
    /// <summary>The Linux member limit (C13).</summary>
    public const int LinuxMemberLimit = 8;

    /// <summary>The Windows member limit when <c>Lab:Pool:MaxWindows</c> is unset (C13).</summary>
    public const int DefaultWindowsMemberLimit = 3;

    private const string HostMemberId = HostProvider.MemberId;
    private const string MemberTargetPrefix = "m:";
    private const string ContainerNamePrefix = "meridian-lab-m-";
    private const string StateFileName = "pool.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _state = new();
    private readonly List<PoolMember> _members = [];
    private readonly Dictionary<string, string> _leaseOf = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<IPoolProvider> _providers;
    private readonly ILabTargetRegistry _registry;
    private readonly FaultTargetGuard _guard;
    private readonly string _stateFile;
    private readonly int _windowsLimit;
    private readonly TimeProvider _time;
    private readonly ILogger<PoolManager> _logger;

    /// <summary>
    /// Creates the manager over the registered providers. Providers are tried in registration order.
    /// </summary>
    public PoolManager(
        LabPaths paths,
        IEnumerable<IPoolProvider> providers,
        ILabTargetRegistry registry,
        FaultTargetGuard guard,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<PoolManager> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configuration);
        _providers = providers.ToArray();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateFile = Path.Combine(paths.State, StateFileName);
        _windowsLimit = int.TryParse(configuration["Lab:Pool:MaxWindows"], out int max) && max > 0 ? max : DefaultWindowsMemberLimit;
    }

    /// <summary>
    /// Matches constraints against provider dimensions. A null constraint matches any value.
    /// </summary>
    /// <param name="dimensions">The provider's dimensions.</param>
    /// <param name="constraints">What the caller asked for.</param>
    public static bool Matches(PoolDimensions dimensions, PoolConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(constraints);
        return (constraints.Os is null || string.Equals(constraints.Os, dimensions.Os, StringComparison.Ordinal))
            && (constraints.MinOsBuild is null || (dimensions.OsBuild is int build && build >= constraints.MinOsBuild))
            && (constraints.Sku is null || string.Equals(constraints.Sku, dimensions.Sku, StringComparison.Ordinal))
            && (constraints.Isolation is null || string.Equals(constraints.Isolation, dimensions.Isolation, StringComparison.Ordinal))
            && (constraints.Kind is null || string.Equals(constraints.Kind, dimensions.Kind, StringComparison.Ordinal))
            && (constraints.Arch is null || string.Equals(constraints.Arch, dimensions.Arch, StringComparison.Ordinal))
            && (constraints.Reboot is null || constraints.Reboot == dimensions.Reboot);
    }

    /// <summary>
    /// Provisions <c>Count</c> members for a lease. Fails with <c>[Lab.PoolExhausted]</c> when the per-OS limit would be passed,
    /// and with <c>[Lab.Unavailable]</c> when no matching provider is available. Partial provisioning is undone.
    /// </summary>
    /// <param name="constraints">The C13 constraints.</param>
    /// <param name="purpose">Why the lease wants members; never a secret.</param>
    /// <param name="leaseId">The lease the members belong to. Never shown in status.</param>
    /// <param name="ct">Cancels provisioning.</param>
    public async Task<Result<IReadOnlyList<PoolMember>>> AcquireAsync(
        PoolConstraints constraints, string purpose, string leaseId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(purpose);
        ArgumentNullException.ThrowIfNull(leaseId);

        if (constraints.Count < 1 || constraints.Count > LinuxMemberLimit)
        {
            return Result<IReadOnlyList<PoolMember>>.Failure("[Lab.InvalidArgument] count must be between 1 and 8");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IPoolProvider? chosen = null;
            var matched = false;
            string? reason = null;
            foreach (IPoolProvider provider in _providers)
            {
                if (!Matches(provider.Dimensions, constraints))
                {
                    continue;
                }

                matched = true;
                Result<bool> available = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
                if (available.IsSuccess && available.Value)
                {
                    chosen = provider;
                    break;
                }

                reason ??= available.IsFailure ? available.Error : "[Lab.Unavailable] provider is not available";
            }

            if (chosen is null)
            {
                return Result<IReadOnlyList<PoolMember>>.Failure(matched && reason is not null
                    ? reason
                    : "[Lab.Unavailable] no provider matches the constraints");
            }

            if (chosen.Dimensions.Kind == "host")
            {
                return await AcquireHostAsync(chosen, leaseId, ct).ConfigureAwait(false);
            }

            if (CountOf(chosen) + constraints.Count > LimitFor(chosen))
            {
                return Result<IReadOnlyList<PoolMember>>.Failure("[Lab.PoolExhausted] the pool has no free members for this provider");
            }

            var created = new List<PoolMember>();
            for (var i = 0; i < constraints.Count; i++)
            {
                string memberId = NextMemberId(chosen.Dimensions.Os);
                Result<PoolMember> provisioned = await ProvisionOneAsync(chosen, constraints, memberId, leaseId, ct).ConfigureAwait(false);
                if (provisioned.IsFailure)
                {
                    foreach (PoolMember partial in created)
                    {
                        _ = await DestroyOneAsync(partial, ct).ConfigureAwait(false);
                    }

                    _ = await SaveAsync(ct).ConfigureAwait(false);
                    return Result<IReadOnlyList<PoolMember>>.Failure(provisioned.Error);
                }

                created.Add(provisioned.Value);
            }

            Result saved = await SaveAsync(ct).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return Result<IReadOnlyList<PoolMember>>.Failure(saved.Error);
            }

            _logger.LogInformation("lab.pool acquired {Count} member(s) from {Provider}", created.Count, chosen.Kind);
            return Result<IReadOnlyList<PoolMember>>.Success(created);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Destroys one container member. Refuses <c>host</c> and any member the registry does not hold (R-BLAST).
    /// </summary>
    /// <param name="memberId">The member id from <c>pool_acquire</c>.</param>
    /// <param name="ct">Cancels the destroy.</param>
    public async Task<Result> ReleaseAsync(string memberId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(memberId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (memberId == HostMemberId)
            {
                return Result.Failure(HostProvider.ReadOnlyError);
            }

            PoolMember? member = FindMember(memberId);
            if (member is null)
            {
                return Result.Failure("[Lab.NotFound] member is not in the pool");
            }

            Result destroyed = await DestroyOneAsync(member, ct).ConfigureAwait(false);
            if (destroyed.IsFailure)
            {
                return destroyed;
            }

            return await SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Recreates a member from its image with the same id: state <c>Resetting</c>, provider reset, back to <c>Leased</c>.
    /// The host is refused.
    /// </summary>
    /// <param name="memberId">The member id.</param>
    /// <param name="ct">Cancels the reset.</param>
    public async Task<Result<PoolMember>> ResetAsync(string memberId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(memberId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (memberId == HostMemberId)
            {
                return Result<PoolMember>.Failure(HostProvider.ReadOnlyError);
            }

            PoolMember? member = FindMember(memberId);
            if (member is null)
            {
                return Result<PoolMember>.Failure("[Lab.NotFound] member is not in the pool");
            }

            Result<ResolvedTarget> resolved = await _guard.ResolveAsync(member.TargetId, "reset", ct).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                return Result<PoolMember>.Failure(resolved.Error);
            }

            Replace(member with { State = MemberState.Resetting });
            IPoolProvider? provider = ProviderFor(member);
            if (provider is null)
            {
                return Result<PoolMember>.Failure("[Lab.Unavailable] no provider holds this member");
            }

            Result<PoolMember> reset = await provider.ResetAsync(member, ct).ConfigureAwait(false);
            if (reset.IsFailure)
            {
                Replace(member with { State = MemberState.Failed });
                _ = await SaveAsync(ct).ConfigureAwait(false);
                return Result<PoolMember>.Failure(reset.Error);
            }

            PoolMember fresh = reset.Value with { MemberId = member.MemberId, TargetId = member.TargetId, State = MemberState.Leased, CreatedAt = _time.GetUtcNow() };
            Replace(fresh);
            Result saved = await SaveAsync(ct).ConfigureAwait(false);
            return saved.IsFailure ? Result<PoolMember>.Failure(saved.Error) : Result<PoolMember>.Success(fresh);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The providers and the members, for <c>pool_list</c>. Each provider's availability is checked now.
    /// </summary>
    /// <param name="ct">Cancels the availability checks.</param>
    public async Task<PoolListing> ListAsync(CancellationToken ct)
    {
        var statuses = new List<PoolProviderStatus>();
        foreach (IPoolProvider provider in _providers)
        {
            Result<bool> available = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
            bool ok = available.IsSuccess && available.Value;
            statuses.Add(new PoolProviderStatus(provider.Kind, ok, ok ? null : available.IsFailure ? available.Error : "not available"));
        }

        return new PoolListing(statuses, Snapshot());
    }

    /// <inheritdoc />
    /// <remarks>Lease end. The entry never carries the lease id, so every container member is destroyed; one lease holds the pool at a time (C13).</remarks>
    public async Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var destroyed = 0;
            foreach (PoolMember member in Snapshot().Where(m => m.Dimensions.Kind == "container"))
            {
                Result result = await DestroyOneAsync(member, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    destroyed++;
                }
            }

            _ = await SaveAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("lab.pool destroyed {Count} member(s) at lease end", destroyed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>Crash recovery: every container listed in <c>pool.json</c> is destroyed, then the file is cleared.</remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        List<PoolMember> listed = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var leftover = 0;
        foreach (PoolMember member in listed.Where(m => m.ContainerName?.StartsWith(ContainerNamePrefix, StringComparison.Ordinal) == true))
        {
            // The registry does not survive a crash, so the listed member is registered again and then goes through the same
            // guard as every other destroy (live name, member label, context, tripwire). A member that fails the guard is kept.
            Result destroyed = Result.Failure("[Lab.Unavailable] no provider holds this member");
            if (ProviderFor(member) is not null)
            {
                _ = _registry.Register(member.TargetId, LabTargetKind.Member, member.MemberId, member.ContainerName);
                destroyed = await DestroyOneAsync(member, cancellationToken).ConfigureAwait(false);
            }

            if (destroyed.IsFailure)
            {
                _registry.Remove(member.TargetId);
                leftover++;
            }
        }

        lock (_state)
        {
            _members.Clear();
            _leaseOf.Clear();
        }

        if (leftover == 0)
        {
            _ = await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("lab.pool {Count} listed member(s) could not be destroyed; pool.json kept", leftover);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (PoolMember member in Snapshot().Where(m => m.State == MemberState.Leased))
        {
            builder.LeasedMembers.Add(member.MemberId);
        }
    }

    private async Task<Result<IReadOnlyList<PoolMember>>> AcquireHostAsync(IPoolProvider host, string leaseId, CancellationToken ct)
    {
        PoolMember? existing = FindMember(HostMemberId);
        if (existing is not null)
        {
            return Result<IReadOnlyList<PoolMember>>.Success([existing]);
        }

        // The host has no container, so it is never registered and never persisted; a lease holds only the read.
        var anyConstraints = new PoolConstraints(null, null, null, null, null, null, null);
        Result<PoolMember> provisioned = await host.ProvisionAsync(anyConstraints, HostMemberId, ct).ConfigureAwait(false);
        if (provisioned.IsFailure)
        {
            return Result<IReadOnlyList<PoolMember>>.Failure(provisioned.Error);
        }

        PoolMember member = provisioned.Value with { State = MemberState.Leased, CreatedAt = _time.GetUtcNow() };
        lock (_state)
        {
            _members.Add(member);
            _leaseOf[member.MemberId] = leaseId;
        }

        return Result<IReadOnlyList<PoolMember>>.Success([member]);
    }

    private async Task<Result<PoolMember>> ProvisionOneAsync(
        IPoolProvider provider, PoolConstraints constraints, string memberId, string leaseId, CancellationToken ct)
    {
        // R-BLAST: the registry entry exists before the container starts; the guard refuses anything the registry lacks.
        string containerName = ContainerNamePrefix + memberId;
        Result registered = _registry.Register(MemberTargetPrefix + memberId, LabTargetKind.Member, memberId, containerName).IsSuccess
            ? Result.Success()
            : Result.Failure("[Lab.Io] member could not be registered");
        if (registered.IsFailure)
        {
            return Result<PoolMember>.Failure(registered.Error);
        }

        Result<PoolMember> provisioned = await provider.ProvisionAsync(constraints, memberId, ct).ConfigureAwait(false);
        if (provisioned.IsFailure)
        {
            _registry.Remove(MemberTargetPrefix + memberId);
            return provisioned;
        }

        PoolMember member = provisioned.Value with
        {
            MemberId = memberId,
            TargetId = MemberTargetPrefix + memberId,
            ContainerName = containerName,
            State = MemberState.Leased,
            CreatedAt = _time.GetUtcNow(),
        };
        lock (_state)
        {
            _members.Add(member);
            _leaseOf[memberId] = leaseId;
        }

        return Result<PoolMember>.Success(member);
    }

    private async Task<Result> DestroyOneAsync(PoolMember member, CancellationToken ct)
    {
        Result<ResolvedTarget> resolved = await _guard.ResolveAsync(member.TargetId, "reset", ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        IPoolProvider? provider = ProviderFor(member);
        Result destroyed = provider is null
            ? Result.Failure("[Lab.Unavailable] no provider holds this member")
            : await provider.DestroyAsync(member, ct).ConfigureAwait(false);
        if (destroyed.IsFailure)
        {
            return destroyed;
        }

        _registry.Remove(member.TargetId);
        lock (_state)
        {
            _members.RemoveAll(m => m.MemberId == member.MemberId);
            _leaseOf.Remove(member.MemberId);
        }

        return Result.Success();
    }

    private IPoolProvider? ProviderFor(PoolMember member) =>
        _providers.FirstOrDefault(p => p.Dimensions.Os == member.Dimensions.Os && p.Dimensions.Kind == member.Dimensions.Kind);

    private int LimitFor(IPoolProvider provider) => provider.Dimensions.Os == "windows" ? _windowsLimit : LinuxMemberLimit;

    private int CountOf(IPoolProvider provider) =>
        Snapshot().Count(m => m.Dimensions.Os == provider.Dimensions.Os && m.Dimensions.Kind == provider.Dimensions.Kind);

    private string NextMemberId(string os)
    {
        string prefix = os == "windows" ? "win" : "lin";
        List<PoolMember> current = Snapshot();
        for (var n = 1; n <= 99; n++)
        {
            string id = $"{prefix}-{n:00}";
            if (!current.Any(m => m.MemberId == id))
            {
                return id;
            }
        }

        return prefix + "-99";
    }

    private PoolMember? FindMember(string memberId)
    {
        lock (_state)
        {
            return _members.FirstOrDefault(m => m.MemberId == memberId);
        }
    }

    private void Replace(PoolMember member)
    {
        lock (_state)
        {
            var index = _members.FindIndex(m => m.MemberId == member.MemberId);
            if (index >= 0)
            {
                _members[index] = member;
            }
        }
    }

    private List<PoolMember> Snapshot()
    {
        lock (_state)
        {
            return _members.ToList();
        }
    }

    /// <summary>
    /// Writes the container members to <c>pool.json</c> through a temp file, so a crash never leaves a half-written file.
    /// </summary>
    private async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            List<PoolMember> containers = Snapshot().Where(m => m.Dimensions.Kind == "container").ToList();
            string json = JsonSerializer.Serialize(containers, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            string temp = _stateFile + ".tmp";
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            File.Move(temp, _stateFile, overwrite: true);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("lab.pool state could not be written: {ExceptionType}", ex.GetType().Name);
            return Result.Failure("[Lab.Io] pool state could not be written");
        }
    }

    private async Task<List<PoolMember>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_stateFile))
        {
            return [];
        }

        try
        {
            string json = await File.ReadAllTextAsync(_stateFile, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<PoolMember>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("lab.pool state could not be read: {ExceptionType}", ex.GetType().Name);
            return [];
        }
    }
}
