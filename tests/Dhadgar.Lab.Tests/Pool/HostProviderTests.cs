using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Pool;

public sealed class HostProviderTests
{
    [Fact]
    public async Task Provision_ReturnsTheHostWithoutAContainer()
    {
        var host = new HostProvider();

        Result<PoolMember> result = await host.ProvisionAsync(new PoolConstraints(null, null, null, null, null, null, null), "ignored", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("host", result.Value.MemberId);
        Assert.Equal("m:host", result.Value.TargetId);
        Assert.Null(result.Value.ContainerName);
        Assert.Equal("host", result.Value.Dimensions.Kind);
    }

    [Fact]
    public async Task Reset_AndDestroy_AreRefusedAsReadOnly()
    {
        var host = new HostProvider();
        PoolMember member = (await host.ProvisionAsync(new PoolConstraints(null, null, null, null, null, null, null), "host", CancellationToken.None)).Value;

        Result<PoolMember> reset = await host.ResetAsync(member, CancellationToken.None);
        Result destroyed = await host.DestroyAsync(member, CancellationToken.None);

        Assert.Equal("[Lab.BlastRadius] host is read-only", reset.Error);
        Assert.Equal("[Lab.BlastRadius] host is read-only", destroyed.Error);
    }

    [Fact]
    public async Task Manager_RefusesToReleaseOrResetTheHost()
    {
        using var pool = new PoolHarness([new HostProvider()]);
        Result<IReadOnlyList<PoolMember>> acquired = await pool.Manager.AcquireAsync(
            new PoolConstraints(null, null, null, null, "host", null, null), "test", "lease-1", CancellationToken.None);
        Assert.True(acquired.IsSuccess, acquired.Error);

        Result released = await pool.Manager.ReleaseAsync("host", CancellationToken.None);
        Result<PoolMember> reset = await pool.Manager.ResetAsync("host", CancellationToken.None);

        Assert.Equal("[Lab.BlastRadius] host is read-only", released.Error);
        Assert.Equal("[Lab.BlastRadius] host is read-only", reset.Error);
        Assert.Null(pool.Registry.Find("m:host"));
    }
}
