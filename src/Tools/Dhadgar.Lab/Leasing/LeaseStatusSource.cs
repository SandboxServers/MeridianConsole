using Dhadgar.Lab.Daemon;

namespace Dhadgar.Lab.Leasing;

/// <summary>
/// Fills the lease fields of <c>GET /status</c> and <c>lab_status</c>. Reports the holder only, never the lease id.
/// </summary>
public sealed class LeaseStatusSource : ILabStatusSource
{
    private readonly LeaseBook _leases;

    /// <summary>
    /// Creates the source over the lease book.
    /// </summary>
    public LeaseStatusSource(LeaseBook leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    /// <inheritdoc />
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        LeaseInfo? info = _leases.Current;
        if (info is not null)
        {
            builder.Lease = new LeaseStatusDto(true, info.Owner, info.Purpose, info.Since, info.ExpiresAt);
        }
    }
}
