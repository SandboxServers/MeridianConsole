using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tests.Processes;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Server;

namespace Dhadgar.Lab.Tests.Daemon;

/// <summary>
/// R-DI: the daemon's service provider is built with scope and build-time validation, and every registered service resolves.
/// </summary>
[Collection("LabHome")]
public sealed class LabDaemonServiceLifetimeTests
{
    [Fact]
    public async Task Services_BuildWithScopeAndBuildValidation_ResolveEveryLabService()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;

            Assert.NotNull(services.GetRequiredService<LeaseBook>());
            Assert.NotNull(services.GetRequiredService<AuditLog>());
            Assert.NotNull(services.GetRequiredService<SecretVault>());
            Assert.NotNull(services.GetRequiredService<Redactor>());
            Assert.NotNull(services.GetRequiredService<LabToolRegistry>());
            string[] registered = services.GetServices<ILabTool>().Select(t => t.Name).ToArray();
            Assert.Equal(registered.Length, registered.Distinct(StringComparer.Ordinal).Count());
            string[] expected = ToolPolicy.Gates.Keys
                .Where(name => !ToolPolicy.PlannedTools.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expected, registered.Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(registered.Length, services.GetServices<McpServerTool>().Count());

            using IServiceScope scope = services.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<LabToolDispatcher>());
            Assert.Same(services.GetRequiredService<LeaseBook>(), scope.ServiceProvider.GetRequiredService<LeaseBook>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LeaseIdArgument_FollowsTheGate()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;

            static string[] RequiredOf(JsonObject schema) =>
                schema["required"] is JsonArray array ? array.Select(item => item!.GetValue<string>()).ToArray() : [];

            // C3, D-ML66: a Leased tool never declares lease_id in its own schema; an OwnLease tool declares it as optional.
            foreach (ILabTool tool in services.GetServices<ILabTool>())
            {
                LabGate? gate = ToolPolicy.GateFor(tool.Name);
                JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
                JsonObject? properties = schema["properties"] as JsonObject;
                if (gate == LabGate.Leased)
                {
                    Assert.Null(properties?["lease_id"]);
                }
                else if (gate == LabGate.OwnLease)
                {
                    Assert.NotNull(properties?["lease_id"]);
                    Assert.DoesNotContain("lease_id", RequiredOf(schema));
                }
            }

            // The MCP projection advertises lease_id as required on every Leased tool.
            foreach (McpServerTool mcp in services.GetServices<McpServerTool>())
            {
                if (ToolPolicy.GateFor(mcp.ProtocolTool.Name) != LabGate.Leased)
                {
                    continue;
                }

                JsonObject advertised = JsonNode.Parse(mcp.ProtocolTool.InputSchema.GetRawText())!.AsObject();
                Assert.Contains("lease_id", RequiredOf(advertised));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Token_WithTrailingNewline_IsVaultedTrimmedAndRedacted()
    {
        // TokenValidator trims before it decodes, so the vault must hold the trimmed token or a tool payload leaks it.
        string root = Path.Combine(Path.GetTempPath(), "lab-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token + "\n",
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            Redactor redactor = daemon.Services.GetRequiredService<Redactor>();
            string redacted = redactor.Redact("payload " + token);

            Assert.DoesNotContain(token, redacted, StringComparison.Ordinal);
            Assert.StartsWith("payload ***", redacted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task LabListenerPorts_FromTheContainer_IsOneSingleton()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;
            LabListenerPorts listeners = services.GetRequiredService<LabListenerPorts>();
            Assert.Same(listeners, services.GetRequiredService<LabListenerPorts>());
            Assert.NotNull(services.GetRequiredService<LabDoctorTool>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact]
    public async Task LogClauseSource_FromTheContainer_ReadsTheSupervisorRing()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;
            LabProcessSupervisor supervisor = services.GetRequiredService<LabProcessSupervisor>();
            const string marker = "ring-di-marker";
            _ = await supervisor.StartAsync(
                SupervisorHarness.ChildSpec("tc-log", relaunch: false, "--lines", "1", "--message", marker, "--sleep-ms", "60000"),
                CancellationToken.None);
            bool logged = await SupervisorHarness.WaitUntilAsync(
                () => supervisor.RingFor("tc-log") is { } ring && ring.Snapshot().Any(line => line.Message.Contains(marker, StringComparison.Ordinal)),
                TimeSpan.FromSeconds(30));
            Assert.True(logged);

            // The registered "log" source must be wired to the daemon's supervisor ring, not an empty lookup.
            IClauseSource source = services.GetServices<IClauseSource>().Single(s => s.Name == "log");
            DateTimeOffset start = DateTimeOffset.UnixEpoch;
            var window = new ClauseWindow(
                start,
                start,
                new Dictionary<string, DateTimeOffset>(),
                new Dictionary<string, DateTimeOffset>(),
                new Dictionary<string, StepOutcome>());
            var clause = new UatClause(
                Id: "c1",
                Text: "clause",
                Source: "log",
                Op: "eq",
                Value: null,
                Pointer: "/count",
                Since: "row_start",
                TimeoutMs: 0,
                PollMs: 500,
                Required: true,
                SourceFields: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["process"] = JsonSerializer.Deserialize<JsonElement>("\"tc-log\""),
                    ["contains"] = JsonSerializer.Deserialize<JsonElement>("\"" + marker + "\""),
                });

            var read = await source.ReadAsync(clause, new UatVars("run1", "row1", "section1", 42, null), window, CancellationToken.None);

            Assert.True(read.IsSuccess, read.Error);
            Assert.Equal(1, read.Value.GetProperty("count").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [SkippableFact]
    public async Task RunLogSource_FromTheContainer_IsTheSupervisorSource()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        string root = Path.Combine(Path.GetTempPath(), "lab-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
            string token = Base64Url.EncodeToString(bytes);
            Array.Clear(bytes);

            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: root,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IRunLogSource logs = daemon.Services.GetRequiredService<IRunLogSource>();
            Assert.IsType<SupervisorRunLogSource>(logs);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
