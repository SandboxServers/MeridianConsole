using Dhadgar.Lab.Tools.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Services area (D-ML65): the build tool and the service tools. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddServicesArea(IServiceCollection services)
    {
        AddLabTool<BuildTool>(services);
        AddLabTool<ServiceStartTool>(services);
        AddLabTool<ServiceStopTool>(services);
        AddLabTool<ServiceRestartTool>(services);
        AddLabTool<ServiceConfigSetTool>(services);
    }
}
