using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Tools.Faults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Faults area (D-ML65): the fault book, its journal, the blast-radius guard and the fault tools. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddFaultsArea(IServiceCollection services)
    {
        // Blast-radius guard (L19): the three checks are separate classes; the guard composes them.
        services.AddSingleton<IDockerInspector, DockerInspector>();
        services.AddSingleton<ITripwire, Tripwire>();
        services.AddSingleton<FaultTargetGuard>();

        // Write-ahead journal and the fault kinds (L19 toxics and proxy disables, partitions over the same proxies).
        services.AddSingleton<IFaultJournal, FaultJournal>();
        services.AddSingleton<IFaultKind, ToxicFault>();
        services.AddSingleton<IFaultKind, ProxyDisableFault>();
        services.AddSingleton<IFaultKind, PartitionFault>();

        // Container, disk, file and database faults (L20).
        services.AddSingleton<IFaultKind, KillFault>();
        services.AddSingleton<IFaultKind, PauseFault>();
        services.AddSingleton<IFaultKind, DiskFillFault>();
        services.AddSingleton<IFaultKind, CorruptFileFault>();
        services.AddSingleton<IFaultKind, DbFault>();

        // One book, several roles: fault targets for the watchdog, the lease-end hook, status, and the hosted sweeper.
        services.AddSingleton<FaultBook>();
        services.AddSingleton<IFaultTargets>(sp => sp.GetRequiredService<FaultBook>());
        services.AddSingleton<ILeaseEndHook>(sp => sp.GetRequiredService<FaultBook>());
        services.AddSingleton<ILabStatusSource>(sp => sp.GetRequiredService<FaultBook>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<FaultBook>());

        AddLabTool<FaultListTargetsTool>(services);
        AddLabTool<FaultsActiveTool>(services);
        AddLabTool<FaultToxicAddTool>(services);
        AddLabTool<FaultProxyDisableTool>(services);
        AddLabTool<FaultPartitionTool>(services);
        AddLabTool<FaultKillTool>(services);
        AddLabTool<FaultPauseTool>(services);
        AddLabTool<FaultDiskFillTool>(services);
        AddLabTool<FaultCorruptFileTool>(services);
        AddLabTool<FaultDbTool>(services);
        AddLabTool<FaultHealTool>(services);
        AddLabTool<FaultResetTool>(services);
    }
}
