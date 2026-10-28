using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Core area (D-ML65): lab status, the lease tools and the doctor. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddCoreArea(IServiceCollection services)
    {
        AddLabTool<LabStatusTool>(services);
        AddLabTool<LeaseAcquireTool>(services);
        AddLabTool<LeaseRenewTool>(services);
        AddLabTool<LeaseReleaseTool>(services);
        AddLabTool<LeaseStatusTool>(services);
        AddLabTool<LabDoctorTool>(services);
    }
}
