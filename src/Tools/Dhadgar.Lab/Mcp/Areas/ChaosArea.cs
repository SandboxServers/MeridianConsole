using Dhadgar.Lab.Tools.Chaos;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Chaos area (L29): the <c>chaos_run</c> tool. Registers only in this file (R-REG). The Gateway pacer and the job runner are
/// registered by the Jobs area.
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddChaosArea(IServiceCollection services)
    {
        AddLabTool<ChaosRunTool>(services);
    }
}
