using System.Text.Json;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// The arguments of the <c>toxic</c> kind. <see cref="Toxicity"/> defaults to 1.0.
/// </summary>
public sealed record ToxicArgs(string Type, string Stream, double? Toxicity = null, Dictionary<string, long>? Attributes = null);

/// <summary>
/// The <c>toxic</c> fault kind (<c>fault_toxic_add</c>): adds one Toxiproxy toxic to a C10 proxy. The toxic is named
/// <c>&lt;fault_id&gt;-&lt;stream&gt;</c>, so heal removes exactly the toxic this fault added.
/// </summary>
public sealed class ToxicFault(ToxiproxyClient toxiproxy) : IFaultKind
{
    private const double DefaultToxicity = 1.0;

    private readonly ToxiproxyClient _toxiproxy = toxiproxy ?? throw new ArgumentNullException(nameof(toxiproxy));

    /// <inheritdoc />
    public string Kind => "toxic";

    /// <inheritdoc />
    public Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<ToxicArgs> parsed = Parse(args);
        if (parsed.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(parsed.Error));
        }

        Result<JsonElement> healData = KindSupport.ToHealData(new ToxicHealData(target.Name, ToxicName(faultId, parsed.Value.Stream)));
        return Task.FromResult(healData);
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<ToxicArgs> parsed = Parse(args);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        Result<ToxicHealData> heal = KindSupport.FromHealData<ToxicHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        ToxicArgs toxic = parsed.Value;
        var spec = new ToxicSpec(
            heal.Value.Toxic,
            toxic.Type,
            toxic.Stream,
            toxic.Toxicity ?? DefaultToxicity,
            toxic.Attributes ?? new Dictionary<string, long>(StringComparer.Ordinal));

        return await _toxiproxy.AddToxicAsync(heal.Value.Proxy, spec, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<ToxicHealData> heal = KindSupport.FromHealData<ToxicHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            // Nothing was written if the heal data never got past prepare; there is nothing to remove.
            return Result.Success();
        }

        Result removed = await _toxiproxy.RemoveToxicAsync(heal.Value.Proxy, heal.Value.Toxic, ct).ConfigureAwait(false);
        return KindSupport.IsNotFound(removed) ? Result.Success() : removed;
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        // Runs after the journal is healed: every toxic left on the proxies is unowned. Reset removes them all.
        Result reset = await _toxiproxy.ResetAsync(ct).ConfigureAwait(false);
        return reset.IsFailure ? Result<int>.Failure(reset.Error) : Result<int>.Success(0);
    }

    /// <summary>
    /// The toxic name for one fault and stream.
    /// </summary>
    public static string ToxicName(string faultId, string stream) => $"{faultId}-{stream}";

    private static Result<ToxicArgs> Parse(JsonElement args)
    {
        ToxicArgs? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ToxicArgs>(args.GetRawText(), LabJson.Options);
        }
        catch (JsonException)
        {
            return Result<ToxicArgs>.Failure("[Lab.InvalidArgument] toxic arguments do not match the tool's schema");
        }

        if (parsed is null)
        {
            return Result<ToxicArgs>.Failure("[Lab.InvalidArgument] toxic arguments do not match the tool's schema");
        }

        IReadOnlyDictionary<string, long> attributes = parsed.Attributes ?? new Dictionary<string, long>(StringComparer.Ordinal);
        Result valid = ToxicValidator.Validate(parsed.Type, parsed.Stream, parsed.Toxicity ?? DefaultToxicity, attributes);
        return valid.IsFailure ? Result<ToxicArgs>.Failure(valid.Error) : Result<ToxicArgs>.Success(parsed);
    }
}
