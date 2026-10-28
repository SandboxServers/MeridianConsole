using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// UAT area (D-ML65): the spec plan tools <c>uat_list_specs</c> and <c>uat_plan</c>, and the clause sources, evaluator,
/// capability probe and runner that <c>uat_run</c> uses. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddUatArea(IServiceCollection services)
    {
        AddLabTool<UatListSpecsTool>(services);
        AddLabTool<UatPlanTool>(services);
        AddLabTool<UatRunTool>(services);
        AddLabTool<UatReportTool>(services);
        AddLabTool<BundleReadTool>(services);

        services.AddSingleton<IClauseSource, ToolSource>();
        services.AddSingleton<IClauseSource, HttpSource>();
        services.AddSingleton<IClauseSource, DbSource>();
        services.AddSingleton<IClauseSource, StepSource>();
        services.AddSingleton<IClauseSource, HumanSource>();
        services.AddSingleton<IClauseSource, CertStoreSource>();
        services.AddSingleton<IClauseSource, RegistrySource>();
        services.AddSingleton<IClauseSource, StateFileSource>();
        // No log ring is exposed by the supervisor yet, so log clauses stay UNVERIFIED until a ring accessor exists.
        services.AddSingleton<IClauseSource>(new LogSource(_ => null));
        services.AddSingleton<ClauseEvaluator>();
        services.AddSingleton<ICapabilityProbe, LabCapabilityProbe>();
        services.AddSingleton<UatRunner>();
    }
}
