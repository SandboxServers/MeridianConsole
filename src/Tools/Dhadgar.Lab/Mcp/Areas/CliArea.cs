using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// CLI area (L37): the <c>cli_run</c> tool, which runs the lab CLI inside a Linux container member. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddCliArea(IServiceCollection services)
    {
        AddLabTool<CliRunTool>(services);
        services.AddSingleton<IPoolMemberPreparer, CliMemberPreparer>();
    }
}
