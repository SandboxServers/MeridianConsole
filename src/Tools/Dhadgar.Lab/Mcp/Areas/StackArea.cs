using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools.Stack;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Stack area (D-ML65): stack up, down, status and data reset. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddStackArea(IServiceCollection services)
    {
        AddLabTool<StackUpTool>(services);
        AddLabTool<StackDownTool>(services);
        AddLabTool<StackStatusTool>(services);
        services.AddSingleton<LabDataReset>();
        AddLabTool<StackResetDataTool>(services);
    }
}
