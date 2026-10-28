using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Contracts.Agents;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Windows;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The member and OS gate of the Windows sources. It reuses the L12 refusal, so a clause and the matching tool answer alike.
/// </summary>
internal static class WindowsSourceGate
{
    /// <summary>Null when the read may run; otherwise the refusal as a failure.</summary>
    public static Result<JsonElement>? Refuse(IReadOnlyDictionary<string, JsonElement> fields)
    {
        Result<string> member = ClauseFields.Optional(fields, "member");
        if (member.IsFailure)
        {
            return Result<JsonElement>.Failure(member.Error);
        }

        Result<object>? refusal = WindowsReadGate.Refuse(member.Value.Length == 0 ? null : member.Value);
        return refusal is null ? null : Result<JsonElement>.Failure(refusal.Value.Error);
    }
}

/// <summary>
/// The <c>certstore</c> source (C7): certificate metadata from one store through <see cref="CertStoreReader"/>, filtered by
/// <c>subject_contains</c>, <c>issuer_contains</c> and <c>issuer_thumbprint</c>. Result shape: <c>{certs, count}</c>.
/// </summary>
public sealed class CertStoreSource : IClauseSource
{
    /// <inheritdoc />
    public string Name => "certstore";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<JsonElement>? refusal = WindowsSourceGate.Refuse(clause.SourceFields);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        Result<string> store = ClauseFields.Required(clause.SourceFields, "store");
        Result<string> subject = ClauseFields.Optional(clause.SourceFields, "subject_contains");
        Result<string> issuer = ClauseFields.Optional(clause.SourceFields, "issuer_contains");
        Result<string> thumbprint = ClauseFields.Optional(clause.SourceFields, "issuer_thumbprint");
        if (store.IsFailure || subject.IsFailure || issuer.IsFailure || thumbprint.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure("[Lab.InvalidArgument] certstore fields must be strings and store is required"));
        }

        Result<IReadOnlyList<CertInfo>> listed = CertStoreReader.List(store.Value);
        if (listed.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(listed.Error));
        }

        List<CertInfo> certs = listed.Value
            .Where(c => subject.Value.Length == 0 || c.Subject.Contains(subject.Value, StringComparison.Ordinal))
            .Where(c => issuer.Value.Length == 0 || c.Issuer.Contains(issuer.Value, StringComparison.Ordinal))
            .Where(c => thumbprint.Value.Length == 0 || string.Equals(c.Thumbprint, thumbprint.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(new { certs, count = certs.Count })));
    }
}

/// <summary>
/// The <c>registry</c> source (C7): the agent registry key through <see cref="AgentRegistryReader"/> (presence and lengths, never
/// values). The only key it reads is the agent key, as <c>registry_read</c> does; another <c>key</c> is refused.
/// </summary>
public sealed class RegistrySource : IClauseSource
{
    private const string AgentKey = @"HKLM\SOFTWARE\Meridian Console\Agent";

    /// <inheritdoc />
    public string Name => "registry";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<JsonElement>? refusal = WindowsSourceGate.Refuse(clause.SourceFields);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        Result<string> key = ClauseFields.Optional(clause.SourceFields, "key");
        if (key.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(key.Error));
        }

        if (key.Value.Length > 0 && !string.Equals(key.Value, AgentKey, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result<JsonElement>.Failure("[Lab.InvalidArgument] registry reads the agent key only"));
        }

        var read = AgentRegistryReader.Read();
        if (read.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(read.Error));
        }

        return Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(read.Value)));
    }
}

/// <summary>
/// The <c>state_file</c> source (C7): the agent state file through <see cref="AgentStateReader"/>, blanked and redacted. Its path is
/// fixed, as in <c>state_file_read</c>, so a <c>file</c> field names nothing the reader could follow.
/// </summary>
public sealed class StateFileSource : IClauseSource
{
    private readonly AgentStateReader _reader;

    /// <summary>
    /// Creates the source over the state reader.
    /// </summary>
    public StateFileSource(AgentStateReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    public string Name => "state_file";

    /// <inheritdoc />
    public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        Result<JsonElement>? refusal = WindowsSourceGate.Refuse(clause.SourceFields);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        var read = _reader.Read();
        if (read.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(read.Error));
        }

        return Task.FromResult(Result<JsonElement>.Success(JsonSerializer.SerializeToElement(read.Value)));
    }
}
