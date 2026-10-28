using Dhadgar.Lab.Results;
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
    }
}
