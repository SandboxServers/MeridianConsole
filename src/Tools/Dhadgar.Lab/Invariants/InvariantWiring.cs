using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Data;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// Collects unexpected-or-expected process exits from the supervisor for I6. Subscribes at construction.
/// </summary>
public sealed class ProcessExitFeed
{
    private const int MaxKept = 500;

    private readonly ConcurrentQueue<ProcessExit> _exits = new();

    /// <summary>
    /// Subscribes to the supervisor's exit event.
    /// </summary>
    public ProcessExitFeed(LabProcessSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        supervisor.Exited += (_, exit) =>
        {
            _exits.Enqueue(exit);
            while (_exits.Count > MaxKept && _exits.TryDequeue(out ProcessExit? _))
            {
            }
        };
    }

    /// <summary>The exits seen so far, oldest first.</summary>
    public IReadOnlyList<ProcessExit> Snapshot() => _exits.ToArray();
}

/// <summary>
/// Forces the exit feed to exist (and so subscribe) when the daemon starts, so no exit is missed before the first checkpoint.
/// </summary>
public sealed class InvariantFeedStarter(ProcessExitFeed feed) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = feed.Snapshot();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// RabbitMQ management reader for I5: <c>GET /api/queues</c> on the loopback management port with the lab user. The password comes
/// from the vault inside <see cref="SecretVault.Use{T}"/>; redirects and proxies are off; the URL must pass <see cref="LabHosts.IsLabInfraEndpoint"/>.
/// </summary>
public sealed class RabbitMqManagementClient : IDisposable
{
    private static readonly Uri QueuesUri = new($"http://127.0.0.1:{LabPorts.RabbitMqManagement}/api/queues");

    private readonly SecretVault _vault;
    private readonly LabCredentialRefs _refs;
    private readonly HttpMessageHandler _handler;
    private readonly HttpClient _http;

    /// <summary>
    /// Creates the client. Tests pass a fake handler.
    /// </summary>
    public RabbitMqManagementClient(SecretVault vault, LabCredentialRefs refs, HttpMessageHandler? handler = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _refs = refs ?? throw new ArgumentNullException(nameof(refs));
        _handler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        _http = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>
    /// Lists every queue with its message count.
    /// </summary>
    public async Task<Result<IReadOnlyList<BrokerQueueInfo>>> ListQueuesAsync(CancellationToken ct)
    {
        if (!LabHosts.IsLabInfraEndpoint(QueuesUri))
        {
            return Result<IReadOnlyList<BrokerQueueInfo>>.Failure("[Lab.Untrusted] management endpoint is not a lab endpoint");
        }

        string? secretRef = _refs.Get(LabCredentialRefs.RabbitMq);
        if (secretRef is null)
        {
            return Result<IReadOnlyList<BrokerQueueInfo>>.Failure("[Lab.NotFound] the stack has no credentials; run stack_up first");
        }

        Result<string> basic = _vault.Use(secretRef, span => Convert.ToBase64String(Encoding.UTF8.GetBytes("lab:" + new string(span))));
        if (basic.IsFailure)
        {
            return Result<IReadOnlyList<BrokerQueueInfo>>.Failure(basic.Error);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, QueuesUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic.Value);
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Result<IReadOnlyList<BrokerQueueInfo>>.Failure("[Lab.Unavailable] RabbitMQ management refused the queue list");
            }

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            var queues = new List<BrokerQueueInfo>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
                long messages = item.TryGetProperty("messages", out JsonElement m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0;
                queues.Add(new BrokerQueueInfo(name, messages));
            }

            return Result<IReadOnlyList<BrokerQueueInfo>>.Success(queues);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return Result<IReadOnlyList<BrokerQueueInfo>>.Failure("[Lab.Unavailable] RabbitMQ management is not reachable");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }
}

/// <summary>
/// Builders for the real delegates behind I1 to I6. Kept apart from <c>InvariantsArea</c> so the area file only registers.
/// </summary>
public static class InvariantWiring
{
    /// <summary>The row cap of an invariant query.</summary>
    public const int QueryMaxRows = 50;

    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>
    /// The SQL delegate for I2 and I3: read-only through <see cref="LabDb"/> (reader role, direct host port).
    /// </summary>
    public static InvariantSqlQuery SqlQuery(LabDb db) =>
        (database, sql, ct) => db.QueryAsync(database, sql, NoParameters, QueryMaxRows, ct);

    /// <summary>The schema probe: true once EF has created its history table in the service's database.</summary>
    internal const string MigratedProbeSql = "select to_regclass('public.\"__EFMigrationsHistory\"') is not null as migrated";

    /// <summary>
    /// The I2/I3 applicability gate (C11, D-ML80): StackNotUp with no lab_reader credential, SchemaNotReady before the service
    /// migrates, otherwise Applicable. A probe failure is returned as a failure (a check error), never as a skip.
    /// </summary>
    public static InvariantDbGate DbGate(LabDb db, LabCredentialRefs refs) =>
        async (database, ct) =>
        {
            if (refs.Get(LabCredentialRefs.LabReader) is null)
            {
                return Result<DbApplicability>.Success(DbApplicability.StackNotUp);
            }

            Result<DbQueryResult> probe = await db.QueryAsync(database, MigratedProbeSql, NoParameters, 1, ct).ConfigureAwait(false);
            if (probe.IsFailure)
            {
                return Result<DbApplicability>.Failure(probe.Error);
            }

            bool migrated = probe.Value.Rows.Count == 1
                && probe.Value.Rows[0].TryGetValue("migrated", out JsonElement value)
                && value.ValueKind == JsonValueKind.True;
            return Result<DbApplicability>.Success(migrated ? DbApplicability.Applicable : DbApplicability.SchemaNotReady);
        };

    /// <summary>True once stack_up has created the lab credentials (I5's queues and proxies exist only then).</summary>
    public static Func<bool> StackCreated(LabCredentialRefs refs) => () => refs.Get(LabCredentialRefs.LabReader) is not null;

    /// <summary>
    /// The daemon-token predicate for I4: true when the line carries the token's bytes. The token is never copied into a string.
    /// </summary>
    internal static Func<string, bool> DaemonTokenPredicate(EdgeState edge) =>
        line =>
        {
            byte[] token = edge.Token;
            if (token.Length < 16 || line.Length < token.Length)
            {
                return false;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(line);
            try
            {
                return bytes.AsSpan().IndexOf(token) >= 0;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        };

    /// <summary>
    /// Names of the labelled containers (<c>docker ps -a --filter label=meridian-lab=1</c>) on the Linux engine.
    /// </summary>
    public static Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> LabelledContainers(IDockerCli docker) =>
        async ct =>
        {
            Result<DockerOutput> run = await docker.RunAsync(
                DockerEngine.Linux,
                ["ps", "-a", "--filter", I5OrphanInvariant.LabelFilter, "--format", "{{.Names}}"],
                ct).ConfigureAwait(false);
            if (run.IsFailure)
            {
                return Result<IReadOnlyList<string>>.Failure(run.Error);
            }

            if (run.Value.ExitCode != 0)
            {
                return Result<IReadOnlyList<string>>.Failure("[Lab.Docker] docker ps failed");
            }

            string[] names = run.Value.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Result<IReadOnlyList<string>>.Success(names);
        };

    /// <summary>
    /// The immediate sub-folder names of <c>LabPaths.FaultBackups</c>.
    /// </summary>
    public static Func<IReadOnlyList<string>> BackupFolders(LabPaths paths) =>
        () =>
        {
            try
            {
                return Directory.Exists(paths.FaultBackups)
                    ? Directory.GetDirectories(paths.FaultBackups).Select(d => Path.GetFileName(d)).ToArray()
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        };

    /// <summary>
    /// Restart observations for I6: every registered container target, inspected live. A container that cannot be inspected is skipped.
    /// </summary>
    public static Func<CancellationToken, Task<Result<IReadOnlyList<RestartObservation>>>> Observations(ILabTargetRegistry registry, IDockerInspector inspector) =>
        async ct =>
        {
            var list = new List<RestartObservation>();
            foreach (LabTarget target in registry.All.Where(t => t.Kind == LabTargetKind.Container))
            {
                Result<ContainerInspection> inspected = await inspector.InspectAsync(target.ContainerId ?? target.Name, ct).ConfigureAwait(false);
                if (inspected.IsSuccess)
                {
                    list.Add(new RestartObservation(target.Name, target.Name, inspected.Value!.RestartCount, inspected.Value.StartedAt));
                }
            }

            return Result<IReadOnlyList<RestartObservation>>.Success(list);
        };

    /// <summary>
    /// Restart excuses taken from the fault history: a kill or pause fault on a target excuses that target's restart.
    /// (svc_start, svc_restart, stack_up and member_reset excuses need a target-bearing audit and are noted in the report.)
    /// </summary>
    public static Func<IReadOnlyList<RestartExcuse>> Excuses(IFaultHistory history) =>
        () => history.Snapshot()
            .Where(f => f.Kind is "kill" or "pause" or "fault_kill" or "fault_pause")
            .Select(f => new RestartExcuse(f.Kind.StartsWith("fault_", StringComparison.Ordinal) ? f.Kind : "fault_" + f.Kind, f.TargetId, f.StartedAt ?? f.HealedAt ?? DateTimeOffset.MinValue))
            .ToArray();
}
