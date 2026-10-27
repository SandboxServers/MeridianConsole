using System.Security.AccessControl;
using System.Security.Principal;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Windows.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Security;

public sealed class WindowsStateFileSecurityTests : IDisposable
{
    private static readonly DateTimeOffset EnrolledAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dhadgar-winstate-" + Guid.NewGuid().ToString("N"));

    public WindowsStateFileSecurityTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void CreateNew_FileHasProtectedDaclWithOnlyTrustedSids()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs

        var sut = new WindowsStateFileSecurity();
        var path = Path.Combine(_directory, "protected.json");

        var created = sut.CreateNew(path);
        Assert.True(created.IsSuccess, created.Error);
        created.Value!.Dispose();

        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);

        var expected = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            WindowsIdentity.GetCurrent().User!,
        };
        var actual = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToList();

        Assert.NotEmpty(actual);
        Assert.All(actual, sid => Assert.Contains(sid, expected));
    }

    [Fact]
    public void CreateNew_ExistingFile_Fails()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs

        var sut = new WindowsStateFileSecurity();
        var path = Path.Combine(_directory, "existing.json");
        File.WriteAllText(path, "planted");

        var created = sut.CreateNew(path);

        Assert.True(created.IsFailure);
        Assert.Contains("[AgentState.Io]", created.Error, StringComparison.Ordinal);
        Assert.Equal("planted", File.ReadAllText(path));
    }

    [Fact]
    public void VerifyTrusted_FileItCreated_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs

        var sut = new WindowsStateFileSecurity();
        var path = Path.Combine(_directory, "trusted.json");
        var created = sut.CreateNew(path);
        Assert.True(created.IsSuccess, created.Error);
        created.Value!.Dispose();

        var verified = sut.VerifyTrusted(path);

        Assert.True(verified.IsSuccess, verified.Error);
    }

    [Fact]
    public void VerifyTrusted_UsersCanWrite_ReturnsUntrusted()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs

        var sut = new WindowsStateFileSecurity();
        var path = Path.Combine(_directory, "writable.json");
        var created = sut.CreateNew(path);
        Assert.True(created.IsSuccess, created.Error);
        created.Value!.Dispose();

        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Write,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);

        var verified = sut.VerifyTrusted(path);

        Assert.True(verified.IsFailure);
        Assert.Contains("[AgentState.Untrusted]", verified.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateStore_WithWindowsSecurity_RoundTrips()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs

        var sut = new FileAgentStateStore(
            Options.Create(new AgentOptions { StateDirectory = _directory }),
            new PathValidator(NullLogger<PathValidator>.Instance),
            new WindowsStateFileSecurity(),
            NullLogger<FileAgentStateStore>.Instance);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), EnrolledAt);

        var save = await sut.SaveAsync(identity);
        var load = await sut.LoadAsync();

        Assert.True(save.IsSuccess, save.Error);
        Assert.True(load.IsSuccess, load.Error);
        Assert.Equal(identity, load.Value!.Identity);

        // The rename from the temp file keeps the protected DACL on the final file
        var statePath = Path.Combine(_directory, FileAgentStateStore.FileName);
        var security = new FileInfo(statePath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
    }
}
