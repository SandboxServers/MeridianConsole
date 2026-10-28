using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Audit;

/// <summary>
/// The audit log writes one JSON line per event under <see cref="LabPaths.Audit"/>, redacts values, keeps no arguments
/// and rolls over to a new file at the UTC day boundary. Each test points <c>MERIDIAN_LAB_HOME</c> at a temp root.
/// </summary>
[Collection("LabHome")]
public sealed class AuditLogTests : IDisposable
{
    private readonly string _root;
    private readonly string? _previousHome;
    private readonly LabPaths _paths;

    public AuditLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lab-audit-" + Path.GetRandomFileName());
        _previousHome = Environment.GetEnvironmentVariable(LabDefaults.HomeEnvVar);
        Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, _root);
        _paths = LabPaths.Create(new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LabDefaults.HomeEnvVar, _previousHome);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RecordCall_OneCall_WritesOneLineForTheDay()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var vault = new SecretVault();
        using var audit = CreateLog(time, vault);

        var result = audit.RecordCall("c1", "lab_status", LabGate.Open, "mcp", null, "ok", 12);

        Assert.True(result.IsSuccess);
        var lines = ReadLines(Path.Combine(_paths.Audit, "audit-20260102.jsonl"));
        Assert.Single(lines);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("lab.call", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("lab_status", document.RootElement.GetProperty("tool").GetString());
        Assert.Equal("Open", document.RootElement.GetProperty("gate").GetString());
        Assert.Equal("ok", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("12", document.RootElement.GetProperty("duration_ms").GetString());
    }

    [Fact]
    public void RecordCall_Line_HasNoArgumentsField()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var vault = new SecretVault();
        using var audit = CreateLog(time, vault);

        audit.RecordCall("c2", "lab_lease_acquire", LabGate.OwnLease, "cli", "s1", "ok", 3);

        var line = ReadLines(Path.Combine(_paths.Audit, "audit-20260102.jsonl")).Single();
        using var document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.TryGetProperty("arguments", out _));
        Assert.False(document.RootElement.TryGetProperty("args", out _));
        Assert.DoesNotContain("arguments", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordCall_LeaseOwnerHoldingVaultValue_IsRedacted()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var vault = new SecretVault();
        var reference = vault.Add(SecretKind.Password, "owner-secret-value");
        using var audit = CreateLog(time, vault);

        audit.RecordCall("c3", "lab_status", null, "mcp", "owner-secret-value", "ok", 1);

        var line = ReadLines(Path.Combine(_paths.Audit, "audit-20260102.jsonl")).Single();
        Assert.DoesNotContain("owner-secret-value", line, StringComparison.Ordinal);
        Assert.Contains("***" + reference, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_EventFields_WrittenAndEnvelopeKept()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var vault = new SecretVault();
        using var audit = CreateLog(time, vault);
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action"] = "acquired",
            ["name"] = "forged",
            ["owner"] = null,
        };

        audit.Emit(new LabEvent("lab.lease", time.GetUtcNow(), fields));

        var line = ReadLines(Path.Combine(_paths.Audit, "audit-20260102.jsonl")).Single();
        using var document = JsonDocument.Parse(line);
        Assert.Equal("lab.lease", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("acquired", document.RootElement.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("owner").ValueKind);
        Assert.Null(audit.LastFailure);
    }

    [Fact]
    public void RecordCall_AcrossUtcMidnight_RollsOverToNewFile()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 23, 59, 30, TimeSpan.Zero));
        using var vault = new SecretVault();
        using var audit = CreateLog(time, vault);

        audit.RecordCall("c4", "lab_status", LabGate.Open, "mcp", null, "ok", 1);
        time.Advance(TimeSpan.FromMinutes(1));
        audit.RecordCall("c5", "lab_status", LabGate.Open, "mcp", null, "ok", 1);

        Assert.Single(ReadLines(Path.Combine(_paths.Audit, "audit-20260102.jsonl")));
        Assert.Single(ReadLines(Path.Combine(_paths.Audit, "audit-20260103.jsonl")));
    }

    [Fact]
    public void RecordCall_AfterDispose_ReturnsFailure()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var vault = new SecretVault();
        var audit = CreateLog(time, vault);
        audit.Dispose();

        var result = audit.RecordCall("c6", "lab_status", LabGate.Open, "mcp", null, "ok", 1);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Audit]", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a day file while the log still holds it open for writing. The log shares read only, so the reader must
    /// allow writers too; <c>File.ReadAllLines</c> would be refused.
    /// </summary>
    private static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private AuditLog CreateLog(TimeProvider time, SecretVault vault) =>
        new(_paths, new PathValidator(NullLogger<PathValidator>.Instance), new Redactor(vault), time);
}
