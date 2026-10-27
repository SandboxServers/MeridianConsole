using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// The values the driver writes into the published agent's <c>appsettings.json</c> (P20 contract table).
/// </summary>
/// <param name="Endpoint">The Gateway address the agent connects to (<c>https://localhost:&lt;gatewayPort&gt;</c>).</param>
/// <param name="HeartbeatIntervalSeconds">Heartbeat interval; the agent accepts 10-300.</param>
/// <param name="ConnectionCheckIntervalSeconds">Connection check interval; at least 10.</param>
/// <param name="StateDirectory">Agent state directory (<c>&lt;runDir&gt;\agent-state</c>).</param>
/// <param name="ServerBasePath">Servers directory (<c>&lt;runDir&gt;\agent-data\Servers</c>).</param>
/// <param name="TempDirectory">Temp directory (<c>&lt;runDir&gt;\agent-data\Temp</c>).</param>
internal sealed record AgentConfig(
    Uri Endpoint,
    int HeartbeatIntervalSeconds,
    int ConnectionCheckIntervalSeconds,
    string StateDirectory,
    string ServerBasePath,
    string TempDirectory);

/// <summary>
/// Edits <c>&lt;runDir&gt;\agent\appsettings.json</c> with <c>System.Text.Json.Nodes</c>, keeping every key the
/// driver does not own. Every path goes through <see cref="RunPaths.Validate"/> (R-RUNDIR).
/// </summary>
internal sealed class AgentPublishConfig
{
    /// <summary>Key for the Gateway endpoint.</summary>
    public const string EndpointKey = "Agent:ControlPlane:Endpoint";

    /// <summary>Key for the heartbeat interval.</summary>
    public const string HeartbeatIntervalKey = "Agent:ControlPlane:HeartbeatIntervalSeconds";

    /// <summary>Key for the connection check interval.</summary>
    public const string ConnectionCheckIntervalKey = "Agent:ControlPlane:ConnectionCheckIntervalSeconds";

    /// <summary>Key for the agent state directory.</summary>
    public const string StateDirectoryKey = "Agent:StateDirectory";

    /// <summary>Key for the servers directory.</summary>
    public const string ServerBasePathKey = "Agent:Process:ServerBasePath";

    /// <summary>Key for the temp directory.</summary>
    public const string TempDirectoryKey = "Agent:Files:TempDirectory";

    /// <summary>Key for the renewal threshold (absent except during U6, D-S221).</summary>
    public const string RenewalThresholdKey = "Agent:Security:CertificateRenewalThresholdDays";

    private const int MinHeartbeatSeconds = 10;
    private const int MaxHeartbeatSeconds = 300;
    private const int MinConnectionCheckSeconds = 10;

    private readonly RunPaths _paths;

    /// <summary>Creates the editor for one run.</summary>
    public AgentPublishConfig(RunPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Writes every contract key from <paramref name="config"/> into the file at <paramref name="appsettingsPath"/>.
    /// Other keys stay as they are.
    /// </summary>
    public Result Apply(string appsettingsPath, AgentConfig config)
    {
        if (config.Endpoint is null || !config.Endpoint.IsAbsoluteUri)
        {
            return Result.Failure("[AgentConfig.Endpoint] endpoint must be an absolute URI");
        }

        if (config.HeartbeatIntervalSeconds is < MinHeartbeatSeconds or > MaxHeartbeatSeconds)
        {
            return Result.Failure("[AgentConfig.Heartbeat] heartbeat interval must be 10-300 seconds");
        }

        if (config.ConnectionCheckIntervalSeconds < MinConnectionCheckSeconds)
        {
            return Result.Failure("[AgentConfig.ConnectionCheck] connection check interval must be at least 10 seconds");
        }

        var state = _paths.Validate(config.StateDirectory);
        if (state.IsFailure)
        {
            return Result.Failure($"[AgentConfig.Path] state directory: {state.Error}");
        }

        var servers = _paths.Validate(config.ServerBasePath);
        if (servers.IsFailure)
        {
            return Result.Failure($"[AgentConfig.Path] servers directory: {servers.Error}");
        }

        var temp = _paths.Validate(config.TempDirectory);
        if (temp.IsFailure)
        {
            return Result.Failure($"[AgentConfig.Path] temp directory: {temp.Error}");
        }

        return Edit(appsettingsPath, root =>
        {
            SetValue(root, EndpointKey, JsonValue.Create(config.Endpoint.GetLeftPart(UriPartial.Authority)));
            SetValue(root, HeartbeatIntervalKey, JsonValue.Create(config.HeartbeatIntervalSeconds));
            SetValue(root, ConnectionCheckIntervalKey, JsonValue.Create(config.ConnectionCheckIntervalSeconds));
            SetValue(root, StateDirectoryKey, JsonValue.Create(state.Value));
            SetValue(root, ServerBasePathKey, JsonValue.Create(servers.Value));
            SetValue(root, TempDirectoryKey, JsonValue.Create(temp.Value));
        });
    }

    /// <summary>
    /// Sets <c>Agent:Security:CertificateRenewalThresholdDays</c>. <c>null</c> removes the key, so the agent uses its default.
    /// </summary>
    public Result SetRenewalThreshold(string appsettingsPath, int? days)
    {
        if (days is < 1)
        {
            return Result.Failure("[AgentConfig.RenewalThreshold] threshold must be at least 1 day");
        }

        return Edit(appsettingsPath, root =>
        {
            if (days is null)
            {
                RemoveValue(root, RenewalThresholdKey);
            }
            else
            {
                SetValue(root, RenewalThresholdKey, JsonValue.Create(days.Value));
            }
        });
    }

    private Result Edit(string appsettingsPath, Action<JsonObject> change)
    {
        var checkedPath = _paths.Validate(appsettingsPath);
        if (checkedPath.IsFailure)
        {
            return Result.Failure($"[AgentConfig.Path] {checkedPath.Error}");
        }

        try
        {
            if (!File.Exists(checkedPath.Value))
            {
                return Result.Failure("[AgentConfig.Missing] appsettings.json was not published");
            }

            var root = JsonNode.Parse(File.ReadAllText(checkedPath.Value)) as JsonObject;
            if (root is null)
            {
                return Result.Failure("[AgentConfig.Corrupt] appsettings.json is not a JSON object");
            }

            change(root);
            File.WriteAllText(checkedPath.Value, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Result.Success();
        }
        catch (JsonException)
        {
            return Result.Failure("[AgentConfig.Corrupt] appsettings.json is not valid JSON");
        }
        catch (IOException ex)
        {
            return Result.Failure($"[AgentConfig.Io] {ex.GetType().Name}");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[AgentConfig.Io] access denied");
        }
    }

    private static void SetValue(JsonObject root, string key, JsonNode? value)
    {
        var parts = key.Split(':');
        var node = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (node[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                node[parts[i]] = child;
            }

            node = child;
        }

        node[parts[^1]] = value;
    }

    private static void RemoveValue(JsonObject root, string key)
    {
        var parts = key.Split(':');
        var node = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (node[parts[i]] is not JsonObject child)
            {
                return;
            }

            node = child;
        }

        node.Remove(parts[^1]);
    }
}
