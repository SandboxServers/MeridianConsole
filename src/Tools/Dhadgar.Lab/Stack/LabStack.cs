using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Stack;

/// <summary>
/// One container of the lab stack as <c>stack_status</c> reports it.
/// </summary>
public sealed record StackContainerDto(string TargetId, string Name, string Image, string Ports, string State, int Restarts);

/// <summary>
/// The lab stack as <c>stack_status</c> reports it.
/// </summary>
public sealed record StackStatusDto(string State, string Network, IReadOnlyList<StackContainerDto> Containers);

/// <summary>
/// Creates, inspects and removes the lab's infrastructure: the network, Postgres, RabbitMQ, Redis and Toxiproxy (C10).
/// Only containers, proxies and databases it registered are ever touched (R-BLAST). Every docker call goes through
/// <see cref="IDockerCli"/> with the Linux context. Passwords reach containers only through a user-only env file.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "LabStack is the name the L08 spec and its tests use for the lab infrastructure owner; renaming it ripples through tools, DI and tests.")]
public sealed class LabStack : ILabStatusSource, IDisposable
{
    private const string PostgresImage = "postgres:17";
    private const string RedisImage = "redis:7";
    private const string ToxiproxyImage = "ghcr.io/shopify/toxiproxy:2.13.0";
    private const string RabbitRepository = "meridian-lab/rabbitmq";
    private const string LoopbackPrefix = "127.0.0.1:";
    private const string SuperUser = "postgres";
    internal static readonly string[] LabDatabaseServices = ["nodes", "identity", "notifications", "discord", "betterauth"];
    private static readonly string[] CredentialNames =
        [LabCredentialRefs.Postgres, LabCredentialRefs.LabOwner, LabCredentialRefs.LabReader, LabCredentialRefs.RabbitMq, LabCredentialRefs.Redis];
    private static readonly TimeSpan ReadinessWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReadinessPoll = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDockerCli _docker;
    private readonly ILabTargetRegistry _targets;
    private readonly ToxiproxyClient _toxiproxy;
    private readonly SecretVault _vault;
    private readonly LabCredentialRefs _refs;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;
    private string _state;

    /// <summary>
    /// Creates the stack over its collaborators. The starting state is <c>degraded</c> when the registry still lists containers.
    /// </summary>
    public LabStack(
        IDockerCli docker,
        ILabTargetRegistry targets,
        ToxiproxyClient toxiproxy,
        SecretVault vault,
        LabCredentialRefs refs,
        LabPaths paths,
        IPathValidator validator,
        TimeProvider time)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _toxiproxy = toxiproxy ?? throw new ArgumentNullException(nameof(toxiproxy));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _refs = refs ?? throw new ArgumentNullException(nameof(refs));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _state = _targets.All.Any(target => target.Kind == LabTargetKind.Container) ? "degraded" : "down";
    }

    /// <summary>
    /// Creates or starts the stack. With <paramref name="wipe"/> the registered containers go first, with their anonymous volumes.
    /// </summary>
    public async Task<Result<StackStatusDto>> UpAsync(bool wipe, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // An env file left by a crash between its write and its delete is removed before anything new is written.
            SweepStaleEnvFiles();
            var hasContainers = _targets.All.Any(target => target.Kind == LabTargetKind.Container);
            if (hasContainers && !wipe && _refs.Get(LabCredentialRefs.Postgres) is null)
            {
                return Result<StackStatusDto>.Failure("[Lab.Docker] the stack's passwords are gone from this daemon; run stack_up with wipe");
            }

            if (wipe)
            {
                var torn = await TearDownAsync(wipe: true, ct).ConfigureAwait(false);
                if (torn.IsFailure)
                {
                    return Result<StackStatusDto>.Failure(torn.Error);
                }
            }

            _state = "starting";
            var brought = await BringUpAsync(ct).ConfigureAwait(false);
            _state = brought.IsSuccess ? "up" : "degraded";
            return brought.IsFailure
                ? Result<StackStatusDto>.Failure(brought.Error)
                : await StatusAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the registered containers. With <paramref name="wipe"/> they are removed with their anonymous volumes, and their
    /// registry entries, proxies, databases and passwords go with them.
    /// </summary>
    public async Task<Result<StackStatusDto>> DownAsync(bool wipe, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var torn = await TearDownAsync(wipe, ct).ConfigureAwait(false);
            if (torn.IsFailure)
            {
                return Result<StackStatusDto>.Failure(torn.Error);
            }

            _state = "down";
            return await StatusAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The stack as it is now: the network, and each registered container with its live state.
    /// </summary>
    public async Task<Result<StackStatusDto>> StatusAsync(CancellationToken ct)
    {
        var network = await Docker(["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        if (network.IsFailure)
        {
            return Result<StackStatusDto>.Failure(network.Error);
        }

        var containers = new List<StackContainerDto>();
        foreach (var target in _targets.All.Where(target => target.Kind == LabTargetKind.Container))
        {
            var inspect = await Docker(
                ["container", "inspect", "--format", "{{.State.Status}}|{{.RestartCount}}|{{.Config.Image}}|{{json .NetworkSettings.Ports}}", target.Name],
                ct).ConfigureAwait(false);
            if (inspect.IsFailure)
            {
                return Result<StackStatusDto>.Failure(inspect.Error);
            }

            if (inspect.Value.ExitCode != 0)
            {
                containers.Add(new StackContainerDto(target.TargetId, target.Name, string.Empty, string.Empty, "missing", 0));
                continue;
            }

            var parts = inspect.Value.StdOut.Trim().Split('|', 4);
            containers.Add(parts.Length < 4
                ? new StackContainerDto(target.TargetId, target.Name, string.Empty, string.Empty, "unknown", 0)
                : new StackContainerDto(
                    target.TargetId,
                    target.Name,
                    parts[2],
                    parts[3],
                    parts[0],
                    int.TryParse(parts[1], out var restarts) ? restarts : 0));
        }

        var state = containers.Count == 0 ? "down" : _state;
        return Result<StackStatusDto>.Success(new StackStatusDto(
            state,
            network.Value.ExitCode == 0 ? "present" : "absent",
            containers));
    }

    /// <inheritdoc />
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.StackState = _state;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private sealed record EnvEntry(string Key, string? SecretRef, string? Literal);

    private Task<Result<DockerOutput>> Docker(string[] args, CancellationToken ct) =>
        _docker.RunAsync(DockerEngine.Linux, args, ct);

    private static string ContainerName(string name) => $"{LabDefaults.ContainerPrefix}{name}";

    private static string DatabaseName(string service) => $"meridian_lab_{service}";

    private static Result<DockerOutput> Require(Result<DockerOutput> call, string message) =>
        call.IsFailure ? call : call.Value.ExitCode == 0 ? call : Result<DockerOutput>.Failure($"[Lab.Docker] {message}");

    private async Task<Result> BringUpAsync(CancellationToken ct)
    {
        var network = await EnsureNetworkAsync(ct).ConfigureAwait(false);
        if (network.IsFailure)
        {
            return network;
        }

        EnsureCredentials();

        var started = await StartContainerAsync(
            "postgres", PostgresImage, ["127.0.0.1:15432:5432"],
            [Secret("POSTGRES_PASSWORD", LabCredentialRefs.Postgres), Secret("LAB_OWNER_PASSWORD", LabCredentialRefs.LabOwner), Secret("LAB_READER_PASSWORD", LabCredentialRefs.LabReader)],
            [], ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started;
        }

        var rabbitImage = await EnsureRabbitImageAsync(ct).ConfigureAwait(false);
        if (rabbitImage.IsFailure)
        {
            return Result.Failure(rabbitImage.Error);
        }

        started = await StartContainerAsync(
            "rabbitmq", rabbitImage.Value, ["127.0.0.1:15673:5672", "127.0.0.1:16672:15672"],
            [Literal("RABBITMQ_DEFAULT_USER", "lab"), Secret("RABBITMQ_DEFAULT_PASS", LabCredentialRefs.RabbitMq)],
            [], ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started;
        }

        // The password reaches redis-server through the environment, never through its argument list.
        started = await StartContainerAsync(
            "redis", RedisImage, ["127.0.0.1:16379:6379"],
            [Secret("REDISCLI_AUTH", LabCredentialRefs.Redis)],
            ["sh", "-c", "exec redis-server --requirepass \"$REDISCLI_AUTH\""], ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started;
        }

        started = await StartContainerAsync(
            "toxiproxy", ToxiproxyImage, ["127.0.0.1:18474:8474", "127.0.0.1:25000:25000", "127.0.0.1:25443:25443"],
            [], [], ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started;
        }

        var ready = await WaitForReadyAsync(ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready;
        }

        var databases = await ProvisionDatabasesAsync(ct).ConfigureAwait(false);
        if (databases.IsFailure)
        {
            return databases;
        }

        foreach (var proxy in ProxyCatalog.ForPacket("L08"))
        {
            var created = await _toxiproxy.CreateProxyAsync(
                new ProxySpec(proxy.Name, $"0.0.0.0:{proxy.ListenPort}", proxy.Upstream, Enabled: true), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created;
            }

            var registered = _targets.Register($"p:{proxy.Name}", LabTargetKind.Proxy, proxy.Name, null);
            if (registered.IsFailure)
            {
                return Result.Failure(registered.Error);
            }
        }

        return Result.Success();
    }

    private async Task<Result> EnsureNetworkAsync(CancellationToken ct)
    {
        var inspect = await Docker(["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        if (inspect.IsFailure)
        {
            return Result.Failure(inspect.Error);
        }

        if (inspect.Value.ExitCode == 0)
        {
            return Result.Success();
        }

        var created = Require(
            await Docker(["network", "create", "--label", $"{LabDefaults.ContainerLabel}=1", LabDefaults.NetworkName], ct).ConfigureAwait(false),
            "could not create the lab network");
        return created.IsFailure ? Result.Failure(created.Error) : Result.Success();
    }

    /// <summary>
    /// Creates a password for every credential that has none. Each is 24 random bytes, base64url, held in the vault.
    /// </summary>
    private void EnsureCredentials()
    {
        foreach (var name in CredentialNames)
        {
            if (_refs.Get(name) is null)
            {
                _refs.Set(name, NewSecret());
            }
        }
    }

    private string NewSecret()
    {
        byte[] random = RandomNumberGenerator.GetBytes(24);
        char[] chars = new char[32];
        try
        {
            _ = Convert.TryToBase64Chars(random, chars, out int written);
            for (var i = 0; i < written; i++)
            {
                chars[i] = chars[i] switch
                {
                    '+' => '-',
                    '/' => '_',
                    _ => chars[i],
                };
            }

            return _vault.Add(SecretKind.Password, chars.AsSpan(0, written));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(random);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
        }
    }

    /// <summary>
    /// Env entry whose value is the vault secret held for <paramref name="credential"/>. Call only after <see cref="EnsureCredentials"/>.
    /// </summary>
    private EnvEntry Secret(string key, string credential) =>
        new(key, _refs.Get(credential) ?? throw new InvalidOperationException("credential was not provisioned before use"), null);

    private static EnvEntry Literal(string key, string value) => new(key, null, value);

    private async Task<Result<string>> EnsureRabbitImageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_paths.RepoRoot))
        {
            return Result<string>.Failure("[Lab.Unavailable] Lab:RepoRoot is not set");
        }

        var dockerfile = _validator.ValidatePath(Path.Combine(_paths.RepoRoot, "tools", "lab", "images", "rabbitmq.Dockerfile"), [_paths.RepoRoot]);
        var context = _validator.ValidatePath(Path.Combine(_paths.RepoRoot, "tools", "lab", "images"), [_paths.RepoRoot]);
        if (dockerfile.IsFailure || context.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the RabbitMQ Dockerfile is outside the repository");
        }

        byte[] source;
        try
        {
            source = await File.ReadAllBytesAsync(dockerfile.Value, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return Result<string>.Failure("[Lab.Io] the RabbitMQ Dockerfile could not be read");
        }

        // The tag is the hash of the Dockerfile, so a changed plugin or base image gets a new image.
        var tag = $"{RabbitRepository}:{Convert.ToHexString(SHA256.HashData(source))[..8].ToLowerInvariant()}";
        var build = Require(
            await Docker(["build", "--label", $"{LabDefaults.ContainerLabel}=1", "-t", tag, "-f", dockerfile.Value, context.Value], ct).ConfigureAwait(false),
            "could not build the RabbitMQ image");
        return build.IsFailure ? Result<string>.Failure(build.Error) : Result<string>.Success(tag);
    }

    private async Task<Result> StartContainerAsync(
        string name,
        string image,
        IReadOnlyList<string> ports,
        IReadOnlyList<EnvEntry> env,
        IReadOnlyList<string> command,
        CancellationToken ct)
    {
        var containerName = ContainerName(name);
        var exists = await Docker(["container", "inspect", "--format", $"{{{{.Id}}}} {{{{index .Config.Labels \"{LabDefaults.ContainerLabel}\"}}}}", containerName], ct).ConfigureAwait(false);
        if (exists.IsFailure)
        {
            return Result.Failure(exists.Error);
        }

        if (exists.Value.ExitCode == 0)
        {
            // A container the lab already created (not wiped): start it again and keep its registration.
            // A name match alone is not ownership: the container must also carry the lab label (R-BLAST).
            string[] inspected = exists.Value.StdOut.Trim().Split(' ', 2);
            if (inspected.Length < 2 || inspected[1] != "1")
            {
                return Result.Failure("[Lab.Docker] the container name is not lab-owned");
            }

            var restarted = Require(await Docker(["start", containerName], ct).ConfigureAwait(false), $"could not start {containerName}");
            return restarted.IsFailure ? Result.Failure(restarted.Error) : Register(name, containerName, inspected[0]);
        }

        if (ports.Any(port => !port.StartsWith(LoopbackPrefix, StringComparison.Ordinal)))
        {
            return Result.Failure("[Lab.Docker] ports must bind to 127.0.0.1");
        }

        string? envFile = null;
        if (env.Count > 0)
        {
            var written = WriteEnvFile(env);
            if (written.IsFailure)
            {
                return Result.Failure(written.Error);
            }

            envFile = written.Value;
        }

        try
        {
            var args = new List<string>
            {
                "run", "-d", "--name", containerName,
                "--label", $"{LabDefaults.ContainerLabel}=1",
                "--restart", "no",
                "--network", LabDefaults.NetworkName,
            };
            foreach (var port in ports)
            {
                args.Add("-p");
                args.Add(port);
            }

            if (envFile is not null)
            {
                args.Add("--env-file");
                args.Add(envFile);
            }

            args.Add(image);
            args.AddRange(command);

            var run = Require(await Docker([.. args], ct).ConfigureAwait(false), $"could not start {containerName}");
            return run.IsFailure ? Result.Failure(run.Error) : Register(name, containerName, run.Value.StdOut.Trim());
        }
        finally
        {
            if (envFile is not null)
            {
                DeleteQuietly(envFile);
            }
        }
    }

    private Result Register(string name, string containerName, string containerId)
    {
        var registered = _targets.Register($"c:{name}", LabTargetKind.Container, containerName, containerId);
        return registered.IsFailure ? Result.Failure(registered.Error) : Result.Success();
    }

    /// <summary>
    /// Writes <c>KEY=value</c> lines to a new file under <see cref="LabPaths.State"/>. The file is created for this run only,
    /// and the caller deletes it. Secret values are copied through the vault's span and zeroed after the write.
    /// </summary>
    private Result<string> WriteEnvFile(IReadOnlyList<EnvEntry> entries)
    {
        var validated = _validator.ValidatePath(Path.Combine(_paths.State, $"env-{Path.GetRandomFileName()}"), [_paths.State]);
        if (validated.IsFailure)
        {
            return Result<string>.Failure(validated.Error);
        }

        var path = validated.Value;
        try
        {
            Directory.CreateDirectory(_paths.State);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                // Owner-only on POSIX hosts; Windows ACLs already restrict the per-user state folder.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            Result<string>? failure = null;
            using (var stream = new FileStream(path, options))
            {
                foreach (var entry in entries)
                {
                    if (entry.SecretRef is null)
                    {
                        WriteEntry(stream, entry.Key, (entry.Literal ?? string.Empty).AsSpan());
                        continue;
                    }

                    var written = _vault.Use(entry.SecretRef, value =>
                    {
                        WriteEntry(stream, entry.Key, value);
                        return true;
                    });
                    if (written.IsFailure)
                    {
                        failure = Result<string>.Failure(written.Error);
                        break;
                    }
                }
            }

            // The stream is closed before the delete: on Windows an open handle makes the delete fail (FileShare.None).
            if (failure is { } failed)
            {
                DeleteQuietly(path);
                return failed;
            }

            return Result<string>.Success(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(path);
            return Result<string>.Failure("[Lab.Io] the container env file could not be written");
        }
    }

    internal static void WriteEntry(Stream stream, string key, ReadOnlySpan<char> value)
    {
        char[] line = new char[key.Length + 1 + value.Length];
        try
        {
            key.AsSpan().CopyTo(line);
            line[key.Length] = '=';
            value.CopyTo(line.AsSpan(key.Length + 1));
            byte[] bytes = new byte[Encoding.UTF8.GetByteCount(line)];
            try
            {
                _ = Encoding.UTF8.GetBytes(line, bytes);
                stream.Write(bytes);
                stream.WriteByte((byte)'\n');
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(line.AsSpan()));
        }
    }

    /// <summary>
    /// Deletes every <c>env-*</c> file left in <see cref="LabPaths.State"/>. Each candidate goes through the path validator first.
    /// </summary>
    private void SweepStaleEnvFiles()
    {
        if (!Directory.Exists(_paths.State))
        {
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(_paths.State, "env-*"))
            {
                Result<string> validated = _validator.ValidatePath(file, [_paths.State]);
                if (validated.IsSuccess)
                {
                    DeleteQuietly(validated.Value!);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a locked or unreadable state folder is reported by the stack operation that needs it.
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the file is in the lab's own state folder and holds no value after the run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above.
        }
    }

    private async Task<Result> WaitForReadyAsync(CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + ReadinessWindow;
        while (true)
        {
            var failing = await FailingProbesAsync(ct).ConfigureAwait(false);
            if (failing.Count == 0)
            {
                return Result.Success();
            }

            if (_time.GetUtcNow() >= deadline)
            {
                return Result.Failure($"[Lab.Docker] infrastructure not ready within 60 s: {string.Join(", ", failing)}");
            }

            await Task.Delay(ReadinessPoll, _time, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<string>> FailingProbesAsync(CancellationToken ct)
    {
        var failing = new List<string>();

        var postgres = await Docker(["exec", ContainerName("postgres"), "pg_isready", "-U", SuperUser], ct).ConfigureAwait(false);
        if (postgres.IsFailure || postgres.Value.ExitCode != 0)
        {
            failing.Add("postgres");
        }

        var rabbit = await Docker(["exec", ContainerName("rabbitmq"), "rabbitmq-diagnostics", "-q", "ping"], ct).ConfigureAwait(false);
        if (rabbit.IsFailure || rabbit.Value.ExitCode != 0)
        {
            failing.Add("rabbitmq");
        }

        var redis = await Docker(["exec", ContainerName("redis"), "redis-cli", "ping"], ct).ConfigureAwait(false);
        if (redis.IsFailure || redis.Value.ExitCode != 0 || !redis.Value.StdOut.Contains("PONG", StringComparison.Ordinal))
        {
            failing.Add("redis");
        }

        var toxiproxy = await _toxiproxy.IsReadyAsync(ct).ConfigureAwait(false);
        if (toxiproxy.IsFailure || !toxiproxy.Value)
        {
            failing.Add("toxiproxy");
        }

        return failing;
    }

    /// <summary>
    /// Creates the lab roles and one database per lab service, owned by <c>lab_owner</c>, readable by <c>lab_reader</c>.
    /// Role passwords reach psql only through the container's environment, referenced as <c>$NAME</c> inside <c>sh -c</c>.
    /// </summary>
    private async Task<Result> ProvisionDatabasesAsync(CancellationToken ct)
    {
        var postgres = ContainerName("postgres");

        var owner = await EnsureRoleAsync(postgres, LabCredentialRefs.LabOwner, "LAB_OWNER_PASSWORD", ct).ConfigureAwait(false);
        if (owner.IsFailure)
        {
            return owner;
        }

        var reader = await EnsureRoleAsync(postgres, LabCredentialRefs.LabReader, "LAB_READER_PASSWORD", ct).ConfigureAwait(false);
        if (reader.IsFailure)
        {
            return reader;
        }

        foreach (var service in LabDatabaseServices)
        {
            var database = DatabaseName(service);
            var present = Require(
                await Docker(["exec", postgres, "psql", "-v", "ON_ERROR_STOP=1", "-U", SuperUser, "-tAc", $"SELECT 1 FROM pg_database WHERE datname = '{database}'"], ct).ConfigureAwait(false),
                "could not query the lab databases");
            if (present.IsFailure)
            {
                return Result.Failure(present.Error);
            }

            if (present.Value.StdOut.Trim() != "1")
            {
                var created = Require(
                    await Docker(["exec", postgres, "psql", "-v", "ON_ERROR_STOP=1", "-U", SuperUser, "-c", $"CREATE DATABASE {database} OWNER {LabCredentialRefs.LabOwner}"], ct).ConfigureAwait(false),
                    $"could not create {database}");
                if (created.IsFailure)
                {
                    return Result.Failure(created.Error);
                }
            }

            var granted = Require(
                await Docker(["exec", postgres, "psql", "-v", "ON_ERROR_STOP=1", "-U", SuperUser, "-c", $"GRANT CONNECT ON DATABASE {database} TO {LabCredentialRefs.LabReader}"], ct).ConfigureAwait(false),
                $"could not grant on {database}");
            if (granted.IsFailure)
            {
                return Result.Failure(granted.Error);
            }

            var schema = Require(
                await Docker(["exec", postgres, "psql", "-v", "ON_ERROR_STOP=1", "-U", SuperUser, "-d", database, "-c",
                    $"GRANT USAGE ON SCHEMA public TO {LabCredentialRefs.LabReader}; ALTER DEFAULT PRIVILEGES FOR ROLE {LabCredentialRefs.LabOwner} IN SCHEMA public GRANT SELECT ON TABLES TO {LabCredentialRefs.LabReader};"], ct).ConfigureAwait(false),
                $"could not set the schema grants on {database}");
            if (schema.IsFailure)
            {
                return Result.Failure(schema.Error);
            }

            var registered = _targets.Register($"d:{service}", LabTargetKind.Database, database, null);
            if (registered.IsFailure)
            {
                return Result.Failure(registered.Error);
            }
        }

        return Result.Success();
    }

    private async Task<Result> EnsureRoleAsync(string postgres, string role, string passwordVariable, CancellationToken ct)
    {
        var present = Require(
            await Docker(["exec", postgres, "psql", "-v", "ON_ERROR_STOP=1", "-U", SuperUser, "-tAc", $"SELECT 1 FROM pg_roles WHERE rolname = '{role}'"], ct).ConfigureAwait(false),
            $"could not query role {role}");
        if (present.IsFailure)
        {
            return Result.Failure(present.Error);
        }

        if (present.Value.StdOut.Trim() == "1")
        {
            return Result.Success();
        }

        // The script names the variable; the value is expanded inside the container from its environment.
        var script = $"psql -v ON_ERROR_STOP=1 -U {SuperUser} -c \"CREATE ROLE {role} LOGIN PASSWORD '${passwordVariable}'\"";
        var created = Require(await Docker(["exec", postgres, "sh", "-c", script], ct).ConfigureAwait(false), $"could not create role {role}");
        return created.IsFailure ? Result.Failure(created.Error) : Result.Success();
    }

    private async Task<Result> TearDownAsync(bool wipe, CancellationToken ct)
    {
        var containers = _targets.All.Where(target => target.Kind == LabTargetKind.Container).Reverse().ToList();
        foreach (var target in containers)
        {
            // R-BLAST: only names this stack registered, and only with the lab prefix.
            if (!target.Name.StartsWith(LabDefaults.ContainerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var call = wipe
                ? await Docker(["rm", "-f", "-v", target.Name], ct).ConfigureAwait(false)
                : await Docker(["stop", target.Name], ct).ConfigureAwait(false);
            if (call.IsFailure)
            {
                return Result.Failure(call.Error);
            }

            if (call.Value.ExitCode != 0)
            {
                // A container that is already gone is the goal; anything else is a failure.
                var stillThere = await Docker(["container", "inspect", "--format", "{{.Id}}", target.Name], ct).ConfigureAwait(false);
                if (stillThere.IsFailure)
                {
                    return Result.Failure(stillThere.Error);
                }

                if (stillThere.Value.ExitCode == 0)
                {
                    return Result.Failure($"[Lab.Docker] could not stop {target.TargetId}");
                }
            }

            if (wipe)
            {
                _targets.Remove(target.TargetId);
            }
        }

        if (!wipe)
        {
            return Result.Success();
        }

        foreach (var target in _targets.All.Where(target => target.Kind is LabTargetKind.Proxy or LabTargetKind.Database).ToList())
        {
            _targets.Remove(target.TargetId);
        }

        foreach (var name in CredentialNames)
        {
            var secretRef = _refs.Get(name);
            if (secretRef is not null)
            {
                _vault.Remove(secretRef);
            }
        }

        _refs.Clear();

        // The network goes only when nothing is attached; docker refuses otherwise and that refusal is expected.
        _ = await Docker(["network", "rm", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        return Result.Success();
    }
}
