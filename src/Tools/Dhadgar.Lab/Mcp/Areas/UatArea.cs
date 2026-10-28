using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// UAT area (D-ML65): the spec plan tools <c>uat_list_specs</c> and <c>uat_plan</c>. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddUatArea(IServiceCollection services)
    {
        AddLabTool<UatListSpecsTool>(services);
        AddLabTool<UatPlanTool>(services);
    }
}
