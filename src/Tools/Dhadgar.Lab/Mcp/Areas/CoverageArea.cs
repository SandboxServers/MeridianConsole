using Dhadgar.Lab.Coverage;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Coverage area (L52): the behavioural coverage observer (<see cref="CoverageCollector"/>, an <see cref="IIngestObserver"/>)
/// and <c>coverage_report</c>. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddCoverageArea(IServiceCollection services)
    {
        services.AddSingleton<IIngestObserver, CoverageCollector>();
        AddLabTool<CoverageReportTool>(services);
    }
}
