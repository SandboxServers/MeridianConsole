using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// Prepares a pool member after the pool provisions or resets it, before a UAT row uses it (D-ML173).
/// </summary>
public interface IPoolMemberPreparer
{
    /// <summary>Prepares one member. A failure is returned, never thrown.</summary>
    Task<Result> PrepareAsync(PoolMember member, CancellationToken ct);
}
