using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Windows area (D-ML65): the agent state reader and the read-only Windows tools. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddWindowsArea(IServiceCollection services)
    {
        services.AddSingleton<AgentStateReader>();
        AddLabTool<CertStoreListTool>(services);
        AddLabTool<RegistryReadTool>(services);
        AddLabTool<StateFileReadTool>(services);
    }
}
