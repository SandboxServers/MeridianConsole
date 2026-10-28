using Dhadgar.Lab.Secrets;
using Dhadgar.Nodes.Data;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// Nodes product adapter for the lab. L01 ships stubs that fail with <c>[Lab.NotImplemented]</c>; L11 implements them.
/// </summary>
public sealed class NodesLabAdapter : INodesLabAdapter
{
    private readonly Func<NodesDbContext> _createContext;
    private readonly SecretVault _vault;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;

    public NodesLabAdapter(Func<NodesDbContext> createContext, SecretVault vault, TimeProvider time, ILoggerFactory loggers)
    {
        _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    public Task<Result<MintedToken>> MintTokenAsync(Guid organizationId, string label, TimeSpan validity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<MintedToken>.Failure("[Lab.NotImplemented] nodes adapter: mint token"));
    }

    public Task<Result<int>> ShiftCertificateTimesAsync(Guid nodeId, TimeSpan shift, IReadOnlyCollection<CertTimeField> fields, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<int>.Failure("[Lab.NotImplemented] nodes adapter: shift certificate times"));
    }
}
