using Dhadgar.Lab.Data;
using Dhadgar.Lab.Http;
using Dhadgar.Lab.Tools.Data;
using Dhadgar.Lab.Tools.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Probes area (D-ML65): read-only SQL and lab-only HTTP. Registers only in this file (R-REG).
/// </summary>
public static partial class LabMcpServer
{
    static partial void AddProbesArea(IServiceCollection services)
    {
        // Reads and probes (L10): read-only SQL on the direct host port, and lab-only HTTP with no redirects.
        services.AddSingleton<LabDb>();
        services.AddSingleton<LabHttp>();
        _ = services.AddHttpClient(LabHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(LabHttp.CreateHandler)
            .ConfigureHttpClient(client => client.Timeout = LabHttp.RequestTimeout);
        AddLabTool<DbQueryTool>(services);
        AddLabTool<HttpRequestTool>(services);
    }
}
