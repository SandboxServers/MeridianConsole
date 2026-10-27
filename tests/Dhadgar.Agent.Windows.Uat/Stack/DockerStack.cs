using System.Diagnostics;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// The PostgreSQL and RabbitMQ containers of one run (P19). Each container is recorded in the manifest before
/// <c>docker run</c>, bound to 127.0.0.1, and labelled <c>meridian-uat=&lt;runId&gt;</c>. Passwords reach the
/// container through the docker CLI's environment (<c>-e NAME</c> with no value), never the command line.
/// </summary>
internal sealed class DockerStack
{
    /// <summary>The label every run container carries.</summary>
    public const string LabelKey = "meridian-uat";

    /// <summary>The database name the Nodes service uses.</summary>
    public const string DatabaseName = "dhadgar_nodes_uat";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReadyBudget = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly string _runId;
    private readonly CleanupManifest _manifest;
    private readonly Redactor _redactor;

    /// <summary>Creates the container set for one run.</summary>
    public DockerStack(string runId, CleanupManifest manifest, Redactor redactor)
    {
        _runId = runId;
        _manifest = manifest;
        _redactor = redactor;
    }

    /// <summary>The Postgres container name for this run.</summary>
    public string PostgresName => $"meridian-uat-pg-{_runId}";

    /// <summary>The RabbitMQ container name for this run.</summary>
    public string RabbitMqName => $"meridian-uat-mq-{_runId}";

    /// <summary>The <c>docker run</c> arguments for the Postgres container. The password is not among them.</summary>
    public static IReadOnlyList<string> PostgresRunArguments(string name, string runId, int hostPort) =>
    [
        "run", "-d", "--name", name,
        "--label", $"{LabelKey}={runId}",
        "-p", $"127.0.0.1:{hostPort}:5432",
        "-e", "POSTGRES_PASSWORD",
        "-e", $"POSTGRES_DB={DatabaseName}",
        "postgres:16",
    ];

    /// <summary>The <c>docker run</c> arguments for the RabbitMQ container. The password is not among them.</summary>
    public static IReadOnlyList<string> RabbitMqRunArguments(string name, string runId, int hostPort) =>
    [
        "run", "-d", "--name", name,
        "--label", $"{LabelKey}={runId}",
        "-p", $"127.0.0.1:{hostPort}:5672",
        "-e", "RABBITMQ_DEFAULT_USER=uat",
        "-e", "RABBITMQ_DEFAULT_PASS",
        "rabbitmq:3-management",
    ];

    /// <summary>
    /// The RabbitMQ readiness probe's <c>docker exec</c> arguments. It runs as the broker user: a root exec creates
    /// <c>/var/lib/rabbitmq/.erlang.cookie</c> as root before the broker reads it, and the broker then exits (P19 defect).
    /// </summary>
    public static IReadOnlyList<string> RabbitMqProbeArguments(string name) =>
        ["exec", "--user", "rabbitmq", name, "rabbitmq-diagnostics", "-q", "ping"];

    /// <summary>Starts Postgres on <paramref name="hostPort"/> and waits until <c>pg_isready</c> succeeds.</summary>
    public async Task<Result> StartPostgresAsync(int hostPort, string password, CancellationToken ct)
    {
        var started = await StartAsync(PostgresName, PostgresRunArguments(PostgresName, _runId, hostPort),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["POSTGRES_PASSWORD"] = password }, ct);
        if (started.IsFailure)
        {
            return started;
        }

        return await WaitReadyAsync(PostgresName,
            ["exec", PostgresName, "pg_isready", "-U", "postgres", "-d", DatabaseName], ct);
    }

    /// <summary>Starts RabbitMQ on 5672 and waits until <c>rabbitmq-diagnostics ping</c> succeeds.</summary>
    public async Task<Result> StartRabbitMqAsync(string password, CancellationToken ct)
    {
        var started = await StartAsync(RabbitMqName, RabbitMqRunArguments(RabbitMqName, _runId, FreePorts.RabbitMqPort),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RABBITMQ_DEFAULT_PASS"] = password }, ct);
        if (started.IsFailure)
        {
            return started;
        }

        return await WaitReadyAsync(RabbitMqName, RabbitMqProbeArguments(RabbitMqName), ct);
    }

    private async Task<Result> StartAsync(
        string name,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        // The manifest entry comes first: a container started by a run that then fails must still be removed.
        var recorded = _manifest.Append(new ContainerEntry(name));
        if (recorded.IsFailure)
        {
            return recorded;
        }

        var output = await CommandRunner.RunAsync("docker", arguments, ReadyBudget, _redactor, null, ct, environment);
        if (output.IsFailure)
        {
            return Result.Failure($"docker run {name} failed: {output.Error}");
        }

        return output.Value.ExitCode == 0
            ? Result.Success()
            : Result.Failure($"docker run {name} exited {output.Value.ExitCode}: {Redact(output.Value.StandardError)}");
    }

    private async Task<Result> WaitReadyAsync(string name, IReadOnlyList<string> probe, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < ReadyBudget)
        {
            ct.ThrowIfCancellationRequested();
            var check = await CommandRunner.RunAsync("docker", probe, CommandTimeout, _redactor, null, ct);
            if (check.IsSuccess && check.Value.ExitCode == 0)
            {
                return Result.Success();
            }

            await Task.Delay(PollInterval, ct);
        }

        var logs = await CommandRunner.RunAsync("docker", ["logs", "--tail", "20", name], CommandTimeout, _redactor, null, ct);
        var tail = logs.IsSuccess ? Redact(logs.Value.StandardOutput + logs.Value.StandardError) : "logs unavailable";
        return Result.Failure($"{name} was not ready within 120 s; last 20 log lines:\n{tail}");
    }

    private string Redact(string text) => string.Join('\n', text.Split('\n').Select(_redactor.Redact).TakeLast(20));
}
