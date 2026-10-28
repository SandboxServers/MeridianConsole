using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// The name and mount check that keeps faults away from product and data containers (R-BLAST, third check).
/// </summary>
public interface ITripwire
{
    /// <summary>
    /// Refuses (<c>[Lab.BlastRadius]</c>) a container named <c>postgres</c>, <c>cache</c> or <c>messaging</c>, a name starting
    /// <c>dhadgar</c> (any case), or any named volume mount. The name may carry its leading <c>/</c>.
    /// </summary>
    Result Check(string name, IReadOnlyList<MountInfo> mounts);
}

/// <summary>
/// The default <see cref="ITripwire"/>.
/// </summary>
public sealed class Tripwire : ITripwire
{
    private static readonly string[] ProtectedNames = ["postgres", "cache", "messaging"];

    /// <inheritdoc />
    public Result Check(string name, IReadOnlyList<MountInfo> mounts)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(mounts);

        var bare = name.TrimStart('/');
        if (Array.IndexOf(ProtectedNames, bare) >= 0 || bare.StartsWith("dhadgar", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("[Lab.BlastRadius] container is a product or data container");
        }

        if (mounts.Any(mount => string.Equals(mount.Type, "volume", StringComparison.Ordinal)))
        {
            return Result.Failure("[Lab.BlastRadius] container mounts a named volume");
        }

        return Result.Success();
    }
}
