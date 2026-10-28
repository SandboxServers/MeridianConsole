using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Jobs area (C4): the job runner, the Gateway pacer (D-ML88), the job cancel hook and <c>job_status</c>, <c>job_cancel</c>.
/// Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddJobsArea(IServiceCollection services)
    {
        services.AddSingleton<GatewayPacer>();
        services.AddSingleton<JobRunner>();
        services.AddSingleton<ILeaseEndHook>(sp => sp.GetRequiredService<JobRunner>());
        services.AddSingleton<ILabStatusSource>(sp => sp.GetRequiredService<JobRunner>());
        AddLabTool<JobStatusTool>(services);
        AddLabTool<JobCancelTool>(services);
        AddLabTool<FlakeCheckTool>(services);
    }
}
