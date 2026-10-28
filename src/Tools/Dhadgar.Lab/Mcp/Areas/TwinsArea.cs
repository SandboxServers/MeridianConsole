using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Twins;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Twins area (L53, C19): the <c>twin_run</c> tool and the runner it uses. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddTwinsArea(IServiceCollection services)
    {
        AddLabTool<TwinRunTool>(services);

        services.AddSingleton<TwinRunner>();
    }
}
