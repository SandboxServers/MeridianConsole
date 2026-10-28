using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Logs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Dhadgar.Lab.Mcp;

/// <summary>
/// Projects every <see cref="ILabTool"/> into the MCP SDK and maps the stateless MCP endpoint. Each projected tool calls
/// <see cref="LabToolDispatcher.InvokeAsync"/> and nothing else, so the policy gate applies to MCP calls too.
/// </summary>
public static class LabMcpServer
{
    private const string LeaseIdField = "lease_id";
    private const int CallIdBytes = 8;

    /// <summary>
    /// Registers the MCP server, the lab tools and their MCP projections. Call once while the daemon is built.
    /// </summary>
    public static void AddLabMcp(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMcpServer().WithHttpTransport(options => options.Stateless = true);

        services.AddSingleton<LabToolRegistry>();
        services.AddSingleton<LabToolDispatcher>();

        AddLabTool<LabStatusTool>(services);
        AddLabTool<LeaseAcquireTool>(services);
        AddLabTool<LeaseRenewTool>(services);
        AddLabTool<LeaseReleaseTool>(services);
        AddLabTool<LeaseStatusTool>(services);
        AddLabTool<LogsTailTool>(services);
    }

    /// <summary>
    /// Maps the MCP endpoint at <see cref="LabDefaults.McpPath"/>. The edge guard runs before it.
    /// </summary>
    public static void MapLabMcp(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.MapMcp(LabDefaults.McpPath);
    }

    private static void AddLabTool<TTool>(IServiceCollection services) where TTool : class, ILabTool
    {
        services.AddSingleton<TTool>();
        services.AddSingleton<ILabTool>(sp => sp.GetRequiredService<TTool>());
        services.AddSingleton<McpServerTool>(sp => new LabMcpTool(
            sp.GetRequiredService<TTool>(),
            sp.GetRequiredService<LabToolDispatcher>()));
    }

    /// <summary>
    /// One lab tool as an MCP tool. Name, description and schema come from the lab tool.
    /// </summary>
    internal sealed class LabMcpTool : McpServerTool
    {
        private readonly ILabTool _tool;
        private readonly LabToolDispatcher _dispatcher;
        private readonly Tool _protocolTool;

        public LabMcpTool(ILabTool tool, LabToolDispatcher dispatcher)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _protocolTool = new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.InputSchema,
            };
        }

        /// <inheritdoc />
        public override Tool ProtocolTool => _protocolTool;

        /// <summary>
        /// Lab tools carry no attribute metadata; the dispatcher's gate table is the only policy.
        /// </summary>
        public override IReadOnlyList<object> Metadata { get; } = [];

        /// <inheritdoc />
        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (request.Params?.Arguments is { } supplied)
            {
                foreach (KeyValuePair<string, JsonElement> pair in supplied)
                {
                    arguments[pair.Key] = pair.Value;
                }
            }

            string? leaseId = arguments.TryGetValue(LeaseIdField, out JsonElement lease) && lease.ValueKind == JsonValueKind.String
                ? lease.GetString()
                : null;

            var context = new LabCallContext(NewCallId(), leaseId, "mcp");
            JsonElement argumentElement = JsonSerializer.SerializeToElement(arguments, LabJson.Options);

            LabToolResult result = await _dispatcher
                .InvokeAsync(_tool.Name, argumentElement, context, cancellationToken)
                .ConfigureAwait(false);

            string text = result.Payload.GetRawText();
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = text }],
                StructuredContent = result.Payload,
                IsError = result.IsError,
            };
        }

        private static string NewCallId()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(CallIdBytes);
            return Convert.ToHexString(bytes);
        }
    }
}
