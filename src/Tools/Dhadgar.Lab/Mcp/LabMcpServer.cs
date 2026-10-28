using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
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
public static partial class LabMcpServer
{
    private const int CallIdBytes = 8;

    // Area hooks (D-ML65, R-REG). A hook with no implementing file compiles away. Order is fixed; never reorder.
    static partial void AddCoreArea(IServiceCollection services);        // L05, L12: lab_status, leases, lab_doctor
    static partial void AddLogsArea(IServiceCollection services);        // L07
    static partial void AddWindowsArea(IServiceCollection services);     // L12
    static partial void AddStackArea(IServiceCollection services);       // L08, L11
    static partial void AddNodesArea(IServiceCollection services);       // L11, L58
    static partial void AddProbesArea(IServiceCollection services);      // L10
    static partial void AddServicesArea(IServiceCollection services);    // L09
    static partial void AddUatArea(IServiceCollection services);         // L13-L17, L22, L40
    static partial void AddEventsArea(IServiceCollection services);      // L48
    static partial void AddFaultsArea(IServiceCollection services);      // L19, L20, L33
    static partial void AddInvariantsArea(IServiceCollection services);  // L21
    static partial void AddHistoryArea(IServiceCollection services);     // L49-L51
    static partial void AddResultsArea(IServiceCollection services);     // L25, L26
    static partial void AddJobsArea(IServiceCollection services);        // L27
    static partial void AddFuzzArea(IServiceCollection services);        // L28, L44
    static partial void AddChaosArea(IServiceCollection services);       // L29
    static partial void AddCoverageArea(IServiceCollection services);    // L52
    static partial void AddTwinsArea(IServiceCollection services);       // L53
    static partial void AddMutantsArea(IServiceCollection services);     // L54, L55
    static partial void AddPoolArea(IServiceCollection services);        // L30, L31
    static partial void AddAgentsArea(IServiceCollection services);      // L32, L43, L59
    static partial void AddAuthStackArea(IServiceCollection services);   // L34
    static partial void AddUiArea(IServiceCollection services);          // L35
    static partial void AddNotifyStackArea(IServiceCollection services); // L36
    static partial void AddCliArea(IServiceCollection services);         // L37
    static partial void AddIdentityArea(IServiceCollection services);    // L56
    static partial void AddTlsArea(IServiceCollection services);         // L41
    static partial void AddFleetArea(IServiceCollection services);       // L42
    static partial void AddEnvelopesArea(IServiceCollection services);   // L57
    static partial void AddPeerArea(IServiceCollection services);        // L58
    static partial void AddVersionsArea(IServiceCollection services);    // L60, L61
    static partial void AddGuidedArea(IServiceCollection services);      // L62

    /// <summary>
    /// Registers the MCP server, the lab tools and their MCP projections. Call once while the daemon is built.
    /// </summary>
    public static void AddLabMcp(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMcpServer().WithHttpTransport(options => options.Stateless = true);

        services.AddSingleton<LabToolRegistry>();
        services.AddSingleton<LabToolDispatcher>();

        AddCoreArea(services);
        AddLogsArea(services);
        AddWindowsArea(services);
        AddStackArea(services);
        AddNodesArea(services);
        AddProbesArea(services);
        AddServicesArea(services);
        AddUatArea(services);
        AddEventsArea(services);
        AddFaultsArea(services);
        AddInvariantsArea(services);
        AddHistoryArea(services);
        AddResultsArea(services);
        AddJobsArea(services);
        AddFuzzArea(services);
        AddChaosArea(services);
        AddCoverageArea(services);
        AddTwinsArea(services);
        AddMutantsArea(services);
        AddPoolArea(services);
        AddAgentsArea(services);
        AddAuthStackArea(services);
        AddUiArea(services);
        AddNotifyStackArea(services);
        AddCliArea(services);
        AddIdentityArea(services);
        AddTlsArea(services);
        AddFleetArea(services);
        AddEnvelopesArea(services);
        AddPeerArea(services);
        AddVersionsArea(services);
        AddGuidedArea(services);
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
    /// The schema an MCP client sees for a tool (C3, D-ML66). A Leased tool advertises <c>lease_id</c> as a required string,
    /// because the dispatcher takes it as an envelope field. Every other gate, and a missing gate, returns the schema unchanged.
    /// The raw schema that the spec validator checks is never changed.
    /// </summary>
    internal static JsonElement AdvertisedSchema(JsonElement schema, LabGate? gate)
    {
        if (gate != LabGate.Leased || JsonNode.Parse(schema.GetRawText()) is not JsonObject root)
        {
            return schema;
        }

        if (root["properties"] is not JsonObject properties)
        {
            properties = new JsonObject();
            root["properties"] = properties;
        }

        properties[LabToolDispatcher.LeaseIdArgument] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "The lease_id from lab_lease_acquire.",
        };

        if (root["required"] is not JsonArray required)
        {
            required = new JsonArray();
            root["required"] = required;
        }

        bool listed = required.Any(item => item is JsonValue value
            && value.TryGetValue(out string? name)
            && string.Equals(name, LabToolDispatcher.LeaseIdArgument, StringComparison.Ordinal));
        if (!listed)
        {
            required.Add(LabToolDispatcher.LeaseIdArgument);
        }

        return JsonSerializer.SerializeToElement(root, LabJson.Options);
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
                InputSchema = AdvertisedSchema(tool.InputSchema, ToolPolicy.GateFor(tool.Name)),
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

            string? leaseId = arguments.TryGetValue(LabToolDispatcher.LeaseIdArgument, out JsonElement lease) && lease.ValueKind == JsonValueKind.String
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
