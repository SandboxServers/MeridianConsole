using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Tools.Invariants;
using Dhadgar.Lab.Uat.Eval;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Invariants area (L21): the <c>invariants_check</c> tool, the monitor over every registered <see cref="IInvariant"/>, the
/// ledger of checkpoints and the <c>invariant</c> clause source. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddInvariantsArea(IServiceCollection services)
    {
        AddLabTool<InvariantsCheckTool>(services);

        services.AddSingleton<InvariantLedger>();
        services.AddSingleton(sp => new InvariantMonitor(sp.GetServices<IInvariant>()));
        services.AddSingleton<IClauseSource, InvariantSource>();
    }
}
