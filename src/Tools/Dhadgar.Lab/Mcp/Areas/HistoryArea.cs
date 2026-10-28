using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// History area (C19): <c>history_run</c>. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddHistoryArea(IServiceCollection services)
    {
        AddLabTool<HistoryRunTool>(services);
    }
}
