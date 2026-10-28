using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Microsoft.Extensions.Logging.Abstractions;
using static Dhadgar.Lab.Tests.Faults.KillFaultTests;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The db fault kind: names and identifiers are refused before any SQL, and the fixed statements are exact.
/// The vault holds no credentials here, so any call that reached SQL would fail with a NotFound, not an InvalidArgument.
/// </summary>
public sealed class DbFaultTests
{
    private const string LabDatabase = "meridian_lab_nodes";

    [Theory]
    [InlineData("postgres")]
    [InlineData("meridian_lab_x; drop")]
    [InlineData("meridian_lab_")]
    [InlineData("meridian_lab_Nodes")]
    [InlineData("meridian_lab_nodes2")]
    [InlineData("dhadgar_platform")]
    public async Task FaultDb_InjectedName_IsRefusedBeforeSql(string name)
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());
        var target = new ResolvedTarget("d:x", "database", name, null, null);

        var prepared = await fault.PrepareAsync(target, Json(new DbArgs("connection_limit", null, 5)), "f1", CancellationToken.None);
        var injected = await fault.InjectAsync(target, Json(new DbArgs("connection_limit", null, 5)), default, "f1", CancellationToken.None);

        Assert.True(prepared.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", prepared.Error, StringComparison.Ordinal);
        Assert.True(injected.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", injected.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("users; drop table x")]
    [InlineData("1users")]
    [InlineData("Users")]
    [InlineData("")]
    public async Task FaultDb_InjectedTable_IsRefusedBeforeSql(string table)
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());

        var result = await fault.PrepareAsync(Target(), Json(new DbArgs("lock_table", table)), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task FaultDb_ConnectionLimitOutOfRange_IsRefused(int limit)
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());

        var result = await fault.InjectAsync(Target(), Json(new DbArgs("connection_limit", null, limit)), default, "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultDb_UnknownMode_IsRefused()
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());

        var result = await fault.PrepareAsync(Target(), Json(new DbArgs("drop_everything")), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionLimitStatement_ForLabDatabase_IsExact()
    {
        Assert.Equal("ALTER DATABASE \"meridian_lab_nodes\" CONNECTION LIMIT 0", DbFault.ConnectionLimitStatement(LabDatabase, 0));
        Assert.Equal("ALTER DATABASE \"meridian_lab_nodes\" CONNECTION LIMIT -1", DbFault.ConnectionLimitStatement(LabDatabase, -1));
    }

    [Fact]
    public void Quote_DoublesEmbeddedQuotes()
    {
        Assert.Equal("\"a\"\"b\"", DbFault.Quote("a\"b"));
    }

    [Fact]
    public void DatabaseOf_AcceptsOnlyLabDatabaseNames()
    {
        Assert.True(DbFault.DatabaseOf(LabDatabase).IsSuccess);
        Assert.True(DbFault.DatabaseOf("meridian_lab_nodes\n").IsFailure);
        Assert.True(DbFault.DatabaseOf((string?)null).IsFailure);
    }

    [Fact]
    public async Task Reconcile_UnregisteredDatabase_IsNeverTouched()
    {
        using var vault = new SecretVault();
        var registry = new FakeRegistry();
        registry.Register("c:gateway", LabTargetKind.Container, "gateway", ContainerId);
        var fault = Fault(vault, registry);

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task Heal_LockTableNeverInjected_IsOk()
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());
        var entry = DbEntry("lock_table", "orders");

        var result = await fault.HealAsync(entry, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Heal_TerminateBackends_IsOk()
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());

        var result = await fault.HealAsync(DbEntry("terminate_backends", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Heal_UnreadableHealData_IsOk()
    {
        using var vault = new SecretVault();
        var fault = Fault(vault, new FakeRegistry());
        var entry = DbEntry("connection_limit", null) with { HealData = Json(new { nonsense = true }) };

        var result = await fault.HealAsync(entry, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static DbFault Fault(SecretVault vault, FakeRegistry registry)
    {
        var connections = new LabConnectionStrings(vault, new LabCredentialRefs());
        var guard = new FaultTargetGuard(registry, Inspector(), new PassingTripwire());
        return new DbFault(connections, registry, guard, NullLogger<DbFault>.Instance);
    }

    private static ResolvedTarget Target() => new("d:nodes", "database", LabDatabase, null, null);

    private static FaultJournalEntry DbEntry(string mode, string? table) =>
        new("f1", "db", "d:nodes", Json(new DbArgs(mode, table)),
            Json(new DbHealData("f1", LabDatabase, mode, table, -1, false)),
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);
}
