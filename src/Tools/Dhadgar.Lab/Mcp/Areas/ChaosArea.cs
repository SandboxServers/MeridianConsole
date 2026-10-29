using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Soak;
using Dhadgar.Lab.Tools.Chaos;
using Dhadgar.Lab.Tools.Soak;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Chaos area (L29): the <c>chaos_run</c> and <c>soak_run</c> tools and the resource sampler for soaks. Registers only in this
/// file (R-REG). The Gateway pacer and the job runner are registered by the Jobs area; the sample store by the Invariants area.
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddChaosArea(IServiceCollection services)
    {
        AddLabTool<ChaosRunTool>(services);
        AddLabTool<SoakRunTool>(services);
        services.AddSingleton(sp => new ResourceSampler(
            sp.GetRequiredService<IDockerCli>(),
            sp.GetRequiredService<ILabTargetRegistry>(),
            InvariantWiring.LabelledContainers(sp.GetRequiredService<IDockerCli>()),
            sp.GetRequiredService<TimeProvider>(),
            ResourceSampler.DaemonHandleCount));
    }
}
