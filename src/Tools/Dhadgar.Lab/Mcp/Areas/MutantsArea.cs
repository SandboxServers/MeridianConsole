using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Mutants;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Mutants area (L54, C19): the <c>mutation_run</c> tool. The job and its builder are reached through the tool and the
/// <c>JobRunner</c> already registered in the Jobs area. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddMutantsArea(IServiceCollection services)
    {
        AddLabTool<MutationRunTool>(services);
    }
}
