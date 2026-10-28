using Dhadgar.Lab.Tools.Observe;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Events area (L48): the observation read tool. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddEventsArea(IServiceCollection services)
    {
        AddLabTool<EventsReadTool>(services);
    }
}
