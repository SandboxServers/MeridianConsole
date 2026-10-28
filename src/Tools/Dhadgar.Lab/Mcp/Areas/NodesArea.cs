using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Lab.Tools.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Nodes area (D-ML65): the nodes adapter and the nodes tools. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddNodesArea(IServiceCollection services)
    {
        // Nodes (L11): the adapter opens a context per call as the owner on the direct host port; mint and shift go through it.
        services.AddSingleton<INodesLabAdapter>(sp => new NodesLabAdapter(
            NodesLabContexts.ForOwner(sp.GetRequiredService<LabConnectionStrings>()),
            sp.GetRequiredService<SecretVault>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>()));
        AddLabTool<NodesTokenMintTool>(services);
        AddLabTool<NodesEnrollProbeTool>(services);
        AddLabTool<NodesCertTimeShiftTool>(services);
    }
}
