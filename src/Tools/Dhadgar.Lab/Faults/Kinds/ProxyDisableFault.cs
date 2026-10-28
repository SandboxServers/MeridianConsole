using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// The <c>proxy_disable</c> fault kind (<c>fault_proxy_disable</c>): disables one C10 proxy, so its listener refuses or drops traffic.
/// Heal enables it again; enabling an enabled proxy is a no-op.
/// </summary>
public sealed class ProxyDisableFault(ToxiproxyClient toxiproxy) : IFaultKind
{
    private readonly ToxiproxyClient _toxiproxy = toxiproxy ?? throw new ArgumentNullException(nameof(toxiproxy));

    /// <inheritdoc />
    public string Kind => "proxy_disable";

    /// <inheritdoc />
    public Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Task.FromResult(KindSupport.ToHealData(new ProxyHealData(target.Name)));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<ProxyHealData> heal = KindSupport.FromHealData<ProxyHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        return await _toxiproxy.SetEnabledAsync(heal.Value.Proxy, false, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<ProxyHealData> heal = KindSupport.FromHealData<ProxyHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result enabled = await _toxiproxy.SetEnabledAsync(heal.Value.Proxy, true, ct).ConfigureAwait(false);
        return KindSupport.IsNotFound(enabled) ? Result.Success() : enabled;
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        // Runs after the journal is healed: a proxy still disabled is unowned. Reset enables every proxy.
        Result reset = await _toxiproxy.ResetAsync(ct).ConfigureAwait(false);
        return reset.IsFailure ? Result<int>.Failure(reset.Error) : Result<int>.Success(0);
    }
}
