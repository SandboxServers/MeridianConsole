using Dhadgar.Lab.Fuzz;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Tools.Fuzz;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Fuzz area (L28): the contract fuzzer's transport over <see cref="LabHttp"/> and the <c>fuzz_run</c> tool. Registers only in
/// this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddFuzzArea(IServiceCollection services)
    {
        services.AddSingleton<IFuzzTransport>(sp => new LabFuzzTransport(sp.GetRequiredService<LabHttp>()));
        AddLabTool<FuzzRunTool>(services);
    }
}
