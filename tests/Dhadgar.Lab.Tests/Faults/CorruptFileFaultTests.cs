using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using static Dhadgar.Lab.Tests.Faults.KillFaultTests;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The <c>corrupt_file</c> fault kind on a temporary lab root (R-PATH, R-ZERO, R-SEED, R-RESULT). The Nodes CA files live
/// under <c>state\nodes-ca</c> and the backups under <c>fault-backups</c>; nothing outside the temporary root is touched.
/// </summary>
public sealed class CorruptFileFaultTests : IDisposable
{
    private static readonly byte[] Original = Encoding.UTF8.GetBytes("-----BEGIN PRIVATE KEY----- original key bytes");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l20-corrupt-" + Guid.NewGuid().ToString("N"));
    private readonly LabPaths _paths;

    public CorruptFileFaultTests()
    {
        _paths = LabPaths.FromRoot(_root);
        Directory.CreateDirectory(Path.Combine(_paths.State, "nodes-ca"));
        File.WriteAllBytes(CaKey, Original);
    }

    private string CaKey => Path.Combine(_paths.State, "nodes-ca", "ca.key");

    private string BackupOf(string faultId) => Path.Combine(_paths.FaultBackups, faultId, "nodes-ca-key.bak");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_CopiesTheFileAndRecordsItsHash()
    {
        var heal = await PrepareAsync(Fault(), "f1", "truncate");

        Assert.Equal(Original, ReadBytes(BackupOf("f1")));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Original)), heal.GetProperty("sha256").GetString());
        Assert.Equal(Original.Length, heal.GetProperty("length").GetInt64());
    }

    [Fact]
    public async Task Truncate_EmptiesTheFile_HealRestoresExactBytes()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f1", "truncate");

        var inject = await fault.InjectAsync(Target(), Args("truncate"), heal, "f1", CancellationToken.None);
        Assert.True(inject.IsSuccess, inject.Error);
        Assert.Empty(ReadBytes(CaKey));

        var healed = await fault.HealAsync(Entry("f1", "truncate", heal), CancellationToken.None);
        Assert.True(healed.IsSuccess, healed.Error);
        Assert.Equal(Original, ReadBytes(CaKey));
        Assert.False(Directory.Exists(Path.Combine(_paths.FaultBackups, "f1")));
    }

    [Fact]
    public async Task Delete_RemovesTheFile_HealRestoresExactBytes()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f1", "delete");

        var inject = await fault.InjectAsync(Target(), Args("delete"), heal, "f1", CancellationToken.None);
        Assert.True(inject.IsSuccess, inject.Error);
        Assert.False(File.Exists(CaKey));

        var healed = await fault.HealAsync(Entry("f1", "delete", heal), CancellationToken.None);
        Assert.True(healed.IsSuccess, healed.Error);
        Assert.Equal(Original, ReadBytes(CaKey));
    }

    [Fact]
    public async Task Garbage_SameSeed_GivesTheSameBytesAndTheSameLength()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f1", "garbage", seed: 7);

        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal, "f1", CancellationToken.None)).IsSuccess);
        byte[] first = ReadBytes(CaKey);
        Assert.Equal(Original.Length, first.Length);
        Assert.NotEqual(Original, first);

        Assert.True((await fault.HealAsync(Entry("f1", "garbage", heal), CancellationToken.None)).IsSuccess);
        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal, "f1", CancellationToken.None)).IsSuccess);
        Assert.Equal(first, ReadBytes(CaKey));
    }

    [Fact]
    public async Task Garbage_SameSeedDifferentFaultIds_WriteTheSameBytes()
    {
        var fault = Fault();
        var heal1 = await PrepareAsync(fault, "f1", "garbage", seed: 7);
        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal1, "f1", CancellationToken.None)).IsSuccess);
        byte[] first = ReadBytes(CaKey);
        Assert.True((await fault.HealAsync(Entry("f1", "garbage", heal1), CancellationToken.None)).IsSuccess);

        var heal2 = await PrepareAsync(fault, "f2", "garbage", seed: 7);
        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal2, "f2", CancellationToken.None)).IsSuccess);

        Assert.Equal(first, ReadBytes(CaKey));
    }

    [Fact]
    public async Task Garbage_DifferentSeeds_WriteDifferentBytes()
    {
        var fault = Fault();
        var heal1 = await PrepareAsync(fault, "f1", "garbage", seed: 7);
        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal1, "f1", CancellationToken.None)).IsSuccess);
        byte[] first = ReadBytes(CaKey);
        Assert.True((await fault.HealAsync(Entry("f1", "garbage", heal1), CancellationToken.None)).IsSuccess);

        var heal2 = await PrepareAsync(fault, "f2", "garbage", seed: 8);
        Assert.True((await fault.InjectAsync(Target(), Args("garbage"), heal2, "f2", CancellationToken.None)).IsSuccess);

        Assert.NotEqual(first, ReadBytes(CaKey));
    }

    [Fact]
    public async Task HealAsync_NeverInjected_IsSuccessAndCleansTheBackup()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f1", "truncate");

        var healed = await fault.HealAsync(Entry("f1", "truncate", heal), CancellationToken.None);

        Assert.True(healed.IsSuccess, healed.Error);
        Assert.Equal(Original, ReadBytes(CaKey));
        Assert.False(Directory.Exists(Path.Combine(_paths.FaultBackups, "f1")));
    }

    [Fact]
    public async Task HealAsync_TamperedBackup_IsIntegrityFailureAndLeavesTheFile()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f1", "truncate");
        await fault.InjectAsync(Target(), Args("truncate"), heal, "f1", CancellationToken.None);
        WriteText(BackupOf("f1"), "tampered");

        var healed = await fault.HealAsync(Entry("f1", "truncate", heal), CancellationToken.None);

        Assert.True(healed.IsFailure);
        Assert.StartsWith("[Lab.Integrity]", healed.Error, StringComparison.Ordinal);
        Assert.Empty(ReadBytes(CaKey));
    }

    [Fact]
    public async Task PrepareAsync_UnknownLogicalFile_IsRefusedBeforeAnyFileIsRead()
    {
        var result = await Fault().PrepareAsync(Target(), Json(new CorruptFileArgs("../../etc/passwd", "truncate")), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_paths.FaultBackups, "f1")));
    }

    [Fact]
    public async Task PrepareAsync_Member_IsUnavailableUntilL33()
    {
        var result = await Fault().PrepareAsync(Target(), Json(new CorruptFileArgs("nodes-ca-key", "truncate", "agent")), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.Unavailable] members arrive in L33", result.Error);
    }

    [Fact]
    public async Task PrepareAsync_UnknownMode_IsRefused()
    {
        var result = await Fault().PrepareAsync(Target(), Json(new CorruptFileArgs("nodes-ca-key", "shred")), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_MissingFile_IsNotFound()
    {
        File.Delete(CaKey);

        var result = await Fault().PrepareAsync(Target(), Args("truncate"), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcileAsync_UnownedBackup_IsRestoredAndFolderDeleted()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f9", "truncate");
        await fault.InjectAsync(Target(), Args("truncate"), heal, "f9", CancellationToken.None);

        var result = await Fault(new FakeJournal()).ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value);
        Assert.Equal(Original, ReadBytes(CaKey));
        Assert.False(Directory.Exists(Path.Combine(_paths.FaultBackups, "f9")));
    }

    [Fact]
    public async Task ReconcileAsync_RestoreFails_KeepsTheBackup_ThenRetrySucceeds()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f9", "truncate");
        await fault.InjectAsync(Target(), Args("truncate"), heal, "f9", CancellationToken.None);
        WriteText(BackupOf("f9"), "tampered");

        var failed = await Fault(new FakeJournal()).ReconcileAsync(CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.StartsWith("[Lab.Integrity]", failed.Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(_paths.FaultBackups, "f9")));

        await File.WriteAllBytesAsync(BackupOf("f9"), Original);
        var retried = await Fault(new FakeJournal()).ReconcileAsync(CancellationToken.None);

        Assert.True(retried.IsSuccess, retried.Error);
        Assert.Equal(1, retried.Value);
        Assert.Equal(Original, ReadBytes(CaKey));
        Assert.False(Directory.Exists(Path.Combine(_paths.FaultBackups, "f9")));
    }

    [Fact]
    public async Task ReconcileAsync_BackupOwnedByJournal_IsLeftAlone()
    {
        var fault = Fault();
        var heal = await PrepareAsync(fault, "f9", "truncate");
        await fault.InjectAsync(Target(), Args("truncate"), heal, "f9", CancellationToken.None);

        var result = await Fault(new FakeJournal(Entry("f9", "truncate", heal))).ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.Empty(ReadBytes(CaKey));
        Assert.True(Directory.Exists(Path.Combine(_paths.FaultBackups, "f9")));
    }

    private CorruptFileFault Fault(FakeJournal? journal = null) =>
        new(_paths, new PathValidator(NullLogger<PathValidator>.Instance), journal ?? new FakeJournal(), NullLogger<CorruptFileFault>.Instance);

    private static async Task<JsonElement> PrepareAsync(CorruptFileFault fault, string faultId, string mode, ulong? seed = null)
    {
        var result = await fault.PrepareAsync(Target(), Json(new CorruptFileArgs("nodes-ca-key", mode, null, seed)), faultId, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static byte[] ReadBytes(string path) => File.ReadAllBytesAsync(path).GetAwaiter().GetResult();

    private static void WriteText(string path, string text) => File.WriteAllTextAsync(path, text).GetAwaiter().GetResult();

    private static JsonElement Args(string mode) => Json(new CorruptFileArgs("nodes-ca-key", mode));

    private static FaultJournalEntry Entry(string faultId, string mode, JsonElement heal) =>
        new(faultId, "corrupt_file", "c:gateway", Args(mode), heal,
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);
}
