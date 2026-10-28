using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// The dispatcher's seven steps as one unit: gates, lease checks, audit start and end, redaction, and the error paths
/// (audit refusal, tool exception, lease loss). Each test points <c>MERIDIAN_LAB_HOME</c> at a temp root.
/// </summary>
[Collection("LabHome")]
public sealed class LabToolDispatcherTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    [Fact]
    public async Task OpenCall_Success_WritesBeginAndEndWithoutArguments()
    {
        var tool = new StubTool("open_tool", OkAsync("{\"ok\":true}"));
        using var rig = new Rig(new() { ["open_tool"] = LabGate.Open }, tool);

        var result = await rig.Dispatcher.InvokeAsync("open_tool", Json("{\"secret_arg\":\"argvalue-123\"}"), Context("c-open"), NoCancel);

        Assert.False(result.IsError);
        var lines = rig.AuditLines();
        Assert.Equal(2, lines.Length);
        Assert.Equal("lab.call.begin", Field(lines[0], "name"));
        Assert.Equal("lab.call", Field(lines[1], "name"));
        Assert.Equal("ok", Field(lines[1], "outcome"));
        var everything = rig.AuditText();
        Assert.DoesNotContain("argvalue-123", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_arg", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VaultSecretInPayload_IsRedacted_AndNeverAudited()
    {
        using var rig = new Rig(new() { ["open_tool"] = LabGate.Open }, new StubTool("open_tool", OkAsync("{\"out\":\"vault-secret-value-1\"}")));
        var reference = rig.Vault.Add(SecretKind.Password, "vault-secret-value-1");

        var result = await rig.Dispatcher.InvokeAsync("open_tool", Json("{}"), Context("c-redact"), NoCancel);

        var payload = result.Payload.GetRawText();
        Assert.DoesNotContain("vault-secret-value-1", payload, StringComparison.Ordinal);
        Assert.Contains("***" + reference, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-secret-value-1", rig.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditStartCannotBeWritten_RefusesBeforeInvoke()
    {
        var tool = new StubTool("open_tool", OkAsync("{}"));
        using var rig = new Rig(new() { ["open_tool"] = LabGate.Open }, tool);
        // A file where the audit directory must be makes every audit write fail.
        await File.WriteAllTextAsync(rig.Paths.Audit, "not a directory");

        var result = await rig.Dispatcher.InvokeAsync("open_tool", Json("{}"), Context("c-audit"), NoCancel);

        Assert.True(result.IsError);
        Assert.Equal("Lab.Unavailable", ErrorCode(result.Payload));
        Assert.Equal(0, tool.Invocations);
    }

    [Fact]
    public async Task ToolThrows_ReturnsInternalWithoutText_AndAuditsOneEnd()
    {
        var tool = new StubTool("open_tool", async (_, _, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("pwd=hunter2hunter2");
        });
        using var rig = new Rig(new() { ["open_tool"] = LabGate.Open }, tool);

        var result = await rig.Dispatcher.InvokeAsync("open_tool", Json("{}"), Context("c-throw"), NoCancel);

        Assert.True(result.IsError);
        Assert.Equal("Lab.Internal", ErrorCode(result.Payload));
        Assert.DoesNotContain("hunter2", result.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", rig.AuditText(), StringComparison.Ordinal);
        var lines = rig.AuditLines();
        Assert.Equal(2, lines.Length);
        Assert.Single(lines, line => Field(line, "name") == "lab.call");
        Assert.Equal("Lab.Internal", Field(lines[1], "outcome"));
    }

    [Fact]
    public async Task UnknownTool_IsAuditedOnce_WithoutTheSuppliedName()
    {
        using var rig = new Rig(new Dictionary<string, LabGate>());

        var result = await rig.Dispatcher.InvokeAsync("no_such_tool", Json("{}"), Context("c-unknown"), NoCancel);

        Assert.Equal("Lab.UnknownTool", ErrorCode(result.Payload));
        var lines = rig.AuditLines();
        Assert.Single(lines);
        Assert.Equal("Lab.UnknownTool", Field(lines[0], "outcome"));
        Assert.Equal("<unknown>", Field(lines[0], "tool"));
        Assert.DoesNotContain("no_such_tool", rig.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeasedWithoutLeaseId_IsRefusedAndAudited()
    {
        var tool = new StubTool("leased_tool", OkAsync("{}"));
        using var rig = new Rig(new() { ["leased_tool"] = LabGate.Leased }, tool);

        var result = await rig.Dispatcher.InvokeAsync("leased_tool", Json("{}"), Context("c-nolease"), NoCancel);

        Assert.Equal("Lab.LeaseRequired", ErrorCode(result.Payload));
        Assert.Equal(0, tool.Invocations);
        var lines = rig.AuditLines();
        Assert.Single(lines);
        Assert.Equal("Lab.LeaseRequired", Field(lines[0], "outcome"));
    }

    [Fact]
    public async Task OwnLease_WrongLeaseId_IsRefusedWithHolder_AndNotInvoked()
    {
        var tool = new StubTool("own_tool", OkAsync("{}"));
        using var rig = new Rig(new() { ["own_tool"] = LabGate.OwnLease }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", null, false, null);
        Assert.True(acquired.IsSuccess);

        var result = await rig.Dispatcher.InvokeAsync("own_tool", Json("{\"lease_id\":\"not-the-lease\"}"), Context("c-wrong"), NoCancel);

        Assert.True(result.IsError);
        Assert.Equal("Lab.LeaseHeld", ErrorCode(result.Payload));
        Assert.Equal("session-a", result.Payload.GetProperty("error").GetProperty("holder").GetProperty("owner").GetString());
        Assert.DoesNotContain(acquired.Value.LeaseId, result.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, tool.Invocations);
    }

    [Fact]
    public async Task OwnLease_RightLeaseId_InvokesWithValidatedId_AndAbsentIdInvokes()
    {
        string? seen = null;
        var tool = new StubTool("own_tool", (_, context, _) =>
        {
            seen = context.LeaseId;
            return Task.FromResult(Ok("{}"));
        });
        using var rig = new Rig(new() { ["own_tool"] = LabGate.OwnLease }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", null, false, null);

        var withId = await rig.Dispatcher.InvokeAsync("own_tool", Json($"{{\"lease_id\":\"{acquired.Value.LeaseId}\"}}"), Context("c-right"), NoCancel);
        Assert.False(withId.IsError);
        Assert.Equal(acquired.Value.LeaseId, seen);

        var withoutId = await rig.Dispatcher.InvokeAsync("own_tool", Json("{}"), Context("c-absent"), NoCancel);
        Assert.False(withoutId.IsError);
        Assert.Equal(2, tool.Invocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leased_LeaseEndsWhileRunning_CancelsToolAndReturnsLeaseLost(bool takeOver)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new StubTool("leased_tool", async (_, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Ok("{}");
        });
        using var rig = new Rig(new() { ["leased_tool"] = LabGate.Leased }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", 60, false, null);
        Assert.True(acquired.IsSuccess);

        var call = rig.Dispatcher.InvokeAsync("leased_tool", Json($"{{\"lease_id\":\"{acquired.Value.LeaseId}\"}}"), Context("c-lost"), NoCancel);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        if (takeOver)
        {
            var taken = rig.Leases.Acquire("session-b", "other", null, true, "takeover test");
            Assert.True(taken.IsSuccess);
        }
        else
        {
            rig.Time.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal(1, rig.Leases.Sweep());
        }

        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.IsError);
        Assert.Equal("Lab.LeaseLost", ErrorCode(result.Payload));
    }

    private static Func<JsonElement, LabCallContext, CancellationToken, Task<Result<JsonElement>>> OkAsync(string json) =>
        (_, _, _) => Task.FromResult(Ok(json));

    private static Result<JsonElement> Ok(string json) => Result<JsonElement>.Success(Json(json));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static LabCallContext Context(string callId) => new(callId, null, "mcp");

    private static string ErrorCode(JsonElement payload) =>
        payload.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;

    private static string Field(string line, string name)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty(name).GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Leased_RealLabTool_StripsLeaseIdBeforeBinding()
    {
        var tool = new EchoTool("echo_leased");
        using var rig = new Rig(new() { ["echo_leased"] = LabGate.Leased }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", null, false, null);
        Assert.True(acquired.IsSuccess);

        var result = await rig.Dispatcher.InvokeAsync("echo_leased", Json($"{{\"lease_id\":\"{acquired.Value.LeaseId}\",\"value\":\"v\"}}"), Context("c-strip"), NoCancel);

        Assert.False(result.IsError, result.Payload.GetRawText());
        Assert.Equal("v", result.Payload.GetProperty("value").GetString());
        Assert.True(result.Payload.GetProperty("has_lease").GetBoolean());
    }

    [Fact]
    public async Task Leased_RealLabTool_DuplicateLeaseIdKeys_AreAllStripped()
    {
        var tool = new EchoTool("echo_dup");
        using var rig = new Rig(new() { ["echo_dup"] = LabGate.Leased }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", null, false, null);
        Assert.True(acquired.IsSuccess);

        string raw = $"{{\"lease_id\":\"{acquired.Value.LeaseId}\",\"lease_id\":\"{acquired.Value.LeaseId}\",\"value\":\"v\"}}";
        var result = await rig.Dispatcher.InvokeAsync("echo_dup", Json(raw), Context("c-dup"), NoCancel);

        Assert.False(result.IsError, result.Payload.GetRawText());
        Assert.Equal("v", result.Payload.GetProperty("value").GetString());
    }

    [Fact]
    public async Task OwnLease_RealLabTool_KeepsLeaseIdForItsArgs()
    {
        var tool = new OwnEchoTool("echo_own");
        using var rig = new Rig(new() { ["echo_own"] = LabGate.OwnLease }, tool);
        var acquired = rig.Leases.Acquire("session-a", "chaos", null, false, null);
        Assert.True(acquired.IsSuccess);

        var result = await rig.Dispatcher.InvokeAsync("echo_own", Json($"{{\"lease_id\":\"{acquired.Value.LeaseId}\"}}"), Context("c-own"), NoCancel);

        Assert.False(result.IsError, result.Payload.GetRawText());
        Assert.Equal(acquired.Value.LeaseId, result.Payload.GetProperty("lease").GetString());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EchoArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record EchoArgs(string Value);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "OwnArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record OwnArgs(string? LeaseId = null);

    /// <summary>
    /// A real lab tool over <see cref="EchoArgs"/>. Reports only whether the call context carries a lease.
    /// </summary>
    private sealed class EchoTool : LabTool<EchoArgs>
    {
        private readonly string _name;

        public EchoTool(string name)
        {
            _name = name;
        }

        public override string Name => _name;

        public override string Description => "test echo";

        protected override Task<Result<object>> ExecuteAsync(EchoArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { value = args.Value, has_lease = context.LeaseId is not null }));
    }

    /// <summary>
    /// A real OwnLease lab tool over <see cref="OwnArgs"/>. Reports the lease id its own arguments carry.
    /// </summary>
    private sealed class OwnEchoTool : LabTool<OwnArgs>
    {
        private readonly string _name;

        public OwnEchoTool(string name)
        {
            _name = name;
        }

        public override string Name => _name;

        public override string Description => "test own echo";

        protected override Task<Result<object>> ExecuteAsync(OwnArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { lease = args.LeaseId }));
    }

    /// <summary>
    /// One tool with a fixed behaviour. Counts how often the dispatcher invoked it.
    /// </summary>
    private sealed class StubTool : ILabTool
    {
        private readonly Func<JsonElement, LabCallContext, CancellationToken, Task<Result<JsonElement>>> _invoke;

        public StubTool(string name, Func<JsonElement, LabCallContext, CancellationToken, Task<Result<JsonElement>>> invoke)
        {
            Name = name;
            _invoke = invoke;
        }

        public string Name { get; }

        public string Description => "test stub";

        public JsonElement InputSchema => Json("{}");

        public int Invocations { get; private set; }

        public Task<Result<JsonElement>> InvokeAsync(JsonElement arguments, LabCallContext context, CancellationToken ct)
        {
            Invocations++;
            return _invoke(arguments, context, ct);
        }
    }

    /// <summary>
    /// A daemon-like set of parts over one temp lab root, with a fake clock.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        private readonly string? _previousHome;

        public Rig(Dictionary<string, LabGate> gates, params ILabTool[] tools)
        {
            Root = Path.Combine(Path.GetTempPath(), "lab-dispatch-" + Path.GetRandomFileName());
            _previousHome = Environment.GetEnvironmentVariable(LabDefaults.HomeEnvVar);
            Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, Root);
            Directory.CreateDirectory(Root);
            Paths = LabPaths.Create(new ConfigurationBuilder().Build());
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
            Vault = new SecretVault();
            Redactor = new Redactor(Vault);
            Audit = new AuditLog(Paths, new PathValidator(NullLogger<PathValidator>.Instance), Redactor, Time);
            Leases = new LeaseBook(Time, Audit, new LeaseOptions());
            Registry = new LabToolRegistry(tools);
            Dispatcher = new LabToolDispatcher(
                Registry,
                Leases,
                Audit,
                Redactor,
                Time,
                name => gates.TryGetValue(name, out LabGate gate) ? gate : null);
        }

        public string Root { get; }

        public LabPaths Paths { get; }

        public FakeTimeProvider Time { get; }

        public SecretVault Vault { get; }

        public Redactor Redactor { get; }

        public AuditLog Audit { get; }

        public LeaseBook Leases { get; }

        public LabToolRegistry Registry { get; }

        public LabToolDispatcher Dispatcher { get; }

        /// <summary>
        /// Every audit line written so far, in file order.
        /// </summary>
        public string[] AuditLines() =>
            Directory.Exists(Paths.Audit)
                ? Directory.GetFiles(Paths.Audit, "audit-*.jsonl").OrderBy(path => path, StringComparer.Ordinal)
                    .SelectMany(ReadShared).ToArray()
                : [];

        /// <summary>
        /// Reads a file the audit log still holds open for writing. The log opens with FileShare.Read, so the reader shares write.
        /// </summary>
        private static string[] ReadShared(string path)
        {
            var lines = new List<string>();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        public string AuditText() => string.Join("\n", AuditLines());

        public void Dispose()
        {
            Audit.Dispose();
            Vault.Dispose();
            Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, _previousHome);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
