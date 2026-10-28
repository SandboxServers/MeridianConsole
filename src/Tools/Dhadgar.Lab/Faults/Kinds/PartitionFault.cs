using System.Text.Json;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// The arguments of the <c>partition</c> kind: the two ends of an edge.
/// </summary>
public sealed record PartitionArgs(string From, string To);

/// <summary>
/// The <c>partition</c> fault kind (<c>fault_partition</c>): disables every C10 proxy on one edge (<c>&lt;from&gt;-&lt;to&gt;</c> or
/// <c>&lt;to&gt;-&lt;from&gt;</c>) under one fault id. Heal enables each of them again.
/// </summary>
public sealed class PartitionFault(ToxiproxyClient toxiproxy) : IFaultKind
{
    private readonly ToxiproxyClient _toxiproxy = toxiproxy ?? throw new ArgumentNullException(nameof(toxiproxy));

    /// <inheritdoc />
    public string Kind => "partition";

    /// <summary>
    /// The C10 proxy names on the edge between two services, in table order. Empty when the edge has no proxy.
    /// </summary>
    public static IReadOnlyList<string> EdgeProxies(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        string forward = $"{from}-{to}";
        string backward = $"{to}-{from}";
        return ProxyCatalog.All
            .Where(proxy => proxy.Name == forward || proxy.Name == backward)
            .Select(proxy => proxy.Name)
            .ToList();
    }

    /// <inheritdoc />
    public Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<PartitionArgs> parsed = Parse(args);
        if (parsed.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(parsed.Error));
        }

        IReadOnlyList<string> proxies = EdgeProxies(parsed.Value.From, parsed.Value.To);
        if (proxies.Count == 0)
        {
            return Task.FromResult(Result<JsonElement>.Failure("[Lab.InvalidArgument] no proxy on that edge"));
        }

        // The guard resolved one proxy of this edge; the rest are C10 rows in the same Toxiproxy container.
        if (!proxies.Contains(target.Name, StringComparer.Ordinal))
        {
            return Task.FromResult(Result<JsonElement>.Failure("[Lab.BlastRadius] target is not on that edge"));
        }

        return Task.FromResult(KindSupport.ToHealData(new PartitionHealData(proxies)));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<PartitionHealData> heal = KindSupport.FromHealData<PartitionHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        foreach (string proxy in heal.Value.Proxies)
        {
            Result disabled = await _toxiproxy.SetEnabledAsync(proxy, false, ct).ConfigureAwait(false);
            if (disabled.IsFailure)
            {
                // Partly injected: BeginAsync heals the entry, and heal tolerates the proxies not yet disabled.
                return disabled;
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<PartitionHealData> heal = KindSupport.FromHealData<PartitionHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result? firstFailure = null;
        foreach (string proxy in heal.Value.Proxies)
        {
            Result enabled = await _toxiproxy.SetEnabledAsync(proxy, true, ct).ConfigureAwait(false);
            if (enabled.IsFailure && !KindSupport.IsNotFound(enabled))
            {
                firstFailure ??= enabled;
            }
        }

        return firstFailure ?? Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        Result reset = await _toxiproxy.ResetAsync(ct).ConfigureAwait(false);
        return reset.IsFailure ? Result<int>.Failure(reset.Error) : Result<int>.Success(0);
    }

    private static Result<PartitionArgs> Parse(JsonElement args)
    {
        PartitionArgs? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PartitionArgs>(args.GetRawText(), LabJson.Options);
        }
        catch (JsonException)
        {
            return Result<PartitionArgs>.Failure("[Lab.InvalidArgument] partition arguments do not match the tool's schema");
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.From) || string.IsNullOrWhiteSpace(parsed.To))
        {
            return Result<PartitionArgs>.Failure("[Lab.InvalidArgument] partition needs from and to");
        }

        return Result<PartitionArgs>.Success(parsed);
    }
}
