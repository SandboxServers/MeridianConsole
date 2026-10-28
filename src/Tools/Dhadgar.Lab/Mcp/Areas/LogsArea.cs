using Dhadgar.Lab.Tools.Logs;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Logs area (D-ML65): the log tail tool. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddLogsArea(IServiceCollection services)
    {
        AddLabTool<LogsTailTool>(services);
    }
}
