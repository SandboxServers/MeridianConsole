using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Triage;
using Dhadgar.Lab.Uat.Bundle;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Results area (C12): the results database service (D-ML91) and <c>results_query</c>. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddResultsArea(IServiceCollection services)
    {
        services.AddSingleton<ResultsService>();
        services.AddSingleton<TriageService>();
        services.AddSingleton<IIngestObserver>(sp => sp.GetRequiredService<TriageService>());
        services.AddSingleton(sp => new DraftWriter(
            sp.GetRequiredService<ResultsService>(),
            sp.GetRequiredService<LabPaths>(),
            sp.GetRequiredService<Dhadgar.Agent.Core.Files.IPathValidator>(),
            new SecretScanner(sp.GetRequiredService<SecretVault>()),
            sp.GetRequiredService<TimeProvider>()));
        AddLabTool<ResultsQueryTool>(services);
        AddLabTool<TriageShowTool>(services);
        AddLabTool<TriageDraftIssueTool>(services);
        AddLabTool<TriageMarkTool>(services);
        AddLabTool<ResultsKnownIssueSetTool>(services);
    }
}
