using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Pool;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Pool area (L30): the machine pool, its providers and the <c>pool_*</c> and <c>member_reset</c> tools. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddPoolArea(IServiceCollection services)
    {
        // One manager, several roles: the pool tools, the lease-end hook, status, and the hosted crash recovery.
        services.AddSingleton<PoolManager>();
        services.AddSingleton<ILeaseEndHook>(sp => sp.GetRequiredService<PoolManager>());
        services.AddSingleton<ILabStatusSource>(sp => sp.GetRequiredService<PoolManager>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PoolManager>());

        // Providers are tried in this order. Only the Linux container and host providers are live in L30.
        services.AddSingleton<IPoolProvider, LinuxContainerProvider>();
        services.AddSingleton<IPoolProvider, HostProvider>();
        services.AddSingleton<IPoolProvider, WindowsContainerProvider>();
        services.AddSingleton<IPoolProvider, VmProvider>();

        AddLabTool<PoolListTool>(services);
        AddLabTool<PoolAcquireTool>(services);
        AddLabTool<PoolReleaseTool>(services);
        AddLabTool<MemberResetTool>(services);
    }
}
