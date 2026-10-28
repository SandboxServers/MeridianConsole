using System.Text.Json;
using Dhadgar.Contracts.Agents;
using Dhadgar.Lab.Windows;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>certstore_list</c>. <c>member</c> is refused until members arrive in L32.
/// </summary>
public sealed record CertStoreListArgs(string Store, string? Member = null);

/// <summary>
/// Arguments of <c>registry_read</c> and <c>state_file_read</c>. <c>member</c> is refused until members arrive in L32.
/// </summary>
public sealed record WindowsReadArgs(string? Member = null);

/// <summary>
/// The result of <c>certstore_list</c>: the store name and the certificate metadata (never a certificate or key).
/// </summary>
public sealed record CertStoreListResult(string Store, IReadOnlyList<CertInfo> Certificates);

/// <summary>
/// The result of <c>registry_read</c>: presence and length of the agent key's values.
/// </summary>
public sealed record RegistryReadResult(RegistryInfo Registry);

/// <summary>
/// The result of <c>state_file_read</c>: the redacted state file.
/// </summary>
public sealed record StateFileReadResult(bool Exists, string Path, JsonElement? Content);

/// <summary>
/// Shared checks for the host read tools: the Windows gate and the member refusal.
/// </summary>
internal static class WindowsReadGate
{
    internal const string MemberRefusal = "[Lab.Unavailable] members arrive in L32";
    internal const string WindowsOnly = "[Lab.Unavailable] this read runs on Windows only";

    internal static Result<object>? Refuse(string? member)
    {
        if (member is not null)
        {
            return Result<object>.Failure(MemberRefusal);
        }

        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        return Result<object>.Failure(WindowsOnly);
    }
}

/// <summary>
/// <c>certstore_list</c> (Open): certificate metadata for one of <see cref="CertStoreReader.StoreNames"/>.
/// </summary>
public sealed class CertStoreListTool : LabTool<CertStoreListArgs>
{
    /// <inheritdoc />
    public override string Name => "certstore_list";

    /// <inheritdoc />
    public override string Description => "Certificate metadata (thumbprint, subject, issuer, serial, validity, has-private-key) for machine_my, machine_root, agent_ca or current_user_my. Never exports a certificate or key.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(CertStoreListArgs args, LabCallContext context, CancellationToken ct)
    {
        var refusal = WindowsReadGate.Refuse(args.Member);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        Result<IReadOnlyList<CertInfo>> listed = CertStoreReader.List(args.Store);
        if (listed.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(listed.Error!));
        }

        return Task.FromResult(Result<object>.Success(new CertStoreListResult(args.Store, listed.Value!)));
    }
}

/// <summary>
/// <c>registry_read</c> (Open): presence and length of the agent registry key's values. No value is returned.
/// </summary>
public sealed class RegistryReadTool : LabTool<WindowsReadArgs>
{
    /// <inheritdoc />
    public override string Name => "registry_read";

    /// <inheritdoc />
    public override string Description => "The agent registry key (HKLM\\SOFTWARE\\Meridian Console\\Agent): value names, kinds, presence and lengths. Values are never returned.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(WindowsReadArgs args, LabCallContext context, CancellationToken ct)
    {
        var refusal = WindowsReadGate.Refuse(args.Member);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        Result<RegistryInfo> read = AgentRegistryReader.Read();
        if (read.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(read.Error!));
        }

        return Task.FromResult(Result<object>.Success(new RegistryReadResult(read.Value!)));
    }
}

/// <summary>
/// <c>state_file_read</c> (Open): the agent state file, blanked and redacted. The path is fixed, never an argument.
/// </summary>
public sealed class StateFileReadTool : LabTool<WindowsReadArgs>
{
    private readonly AgentStateReader _reader;

    /// <summary>
    /// Creates the tool over the state reader.
    /// </summary>
    public StateFileReadTool(AgentStateReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    public override string Name => "state_file_read";

    /// <inheritdoc />
    public override string Description => "The agent state file under %ProgramData% as JSON, with token, key and secret properties blanked and every value redacted.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(WindowsReadArgs args, LabCallContext context, CancellationToken ct)
    {
        var refusal = WindowsReadGate.Refuse(args.Member);
        if (refusal is not null)
        {
            return Task.FromResult(refusal.Value);
        }

        Result<AgentStateSnapshot> read = _reader.Read();
        if (read.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(read.Error!));
        }

        AgentStateSnapshot snapshot = read.Value!;
        return Task.FromResult(Result<object>.Success(new StateFileReadResult(snapshot.Exists, snapshot.Path, snapshot.Content)));
    }
}
