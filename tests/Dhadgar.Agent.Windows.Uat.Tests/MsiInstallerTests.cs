using System.Text;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The <c>msiexec</c> argument lists, the token handling and the exit-code rules of <see cref="MsiInstaller"/>, with a
/// fake for <c>msiexec</c>. No test runs an installer.
/// </summary>
public class MsiInstallerTests : IDisposable
{
    private const string Token = "uat-enrollment-token-7f3a9c";

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-msiinst-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly Redactor _redactor = new();
    private readonly List<IReadOnlyList<string>> _calls = [];
    private readonly Uri _endpoint = new("https://localhost:8443/");

    public MsiInstallerTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        _redactor.AddSecret(Token, SecretKind.EnrollmentToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private string MsiPath => Path.Combine(_paths.RunDir, "msi", "Dhadgar.Agent.Windows.msi");

    private MintedToken Minted => new(Guid.NewGuid(), Token);

    private MsiInstaller Installer(int exitCode, string? stdout = null) => new(_paths, _redactor, args =>
    {
        _calls.Add(args);
        return Result<CommandOutput>.Success(new CommandOutput(exitCode, stdout ?? string.Empty, string.Empty));
    });

    [Fact]
    public void Install_PassesTheExactArgumentList()
    {
        var log = Path.Combine(_paths.Logs, "msi-install.log");

        var result = Installer(0).Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "/i", MsiPath, "/qn", "/norestart", "/l*v", log,
                $"ENROLLMENT_TOKEN={Token}",
                "CONTROL_PLANE_ENDPOINT=https://localhost:8443/",
            ],
            _calls.Single());
    }

    [Fact]
    public void Install_TokenIsOnlyTheSingleEnrollmentTokenItem()
    {
        Installer(0).Install(MsiPath, Minted, _endpoint);

        var args = _calls.Single();
        var carriers = args.Where(a => a.Contains(Token, StringComparison.Ordinal)).ToList();
        Assert.Equal([$"ENROLLMENT_TOKEN={Token}"], carriers);
    }

    [Fact]
    public void Install_LoggedCommandLineIsRedacted()
    {
        var result = Installer(0).Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsSuccess);
        Assert.Contains("ENROLLMENT_TOKEN=***EnrollmentToken***", result.Value.LoggedCommandLine, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, result.Value.LoggedCommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_ExitCode3010_SucceedsWithRebootWarning()
    {
        var result = Installer(3010).Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsSuccess);
        Assert.Equal(3010, result.Value.ExitCode);
        Assert.Single(result.Value.Warnings);
        Assert.Contains("reboot requested", result.Value.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Install_ExitCode1603_FailsCarryingTheCodeAndTheRedactedLogTail()
    {
        Directory.CreateDirectory(_paths.Logs);
        File.WriteAllText(
            Path.Combine(_paths.Logs, "msi-install.log"),
            "MSI (s) Verbose\r\nInfo 1 start\r\nError 1721 Return value 3 for token " + Token + "\r\n",
            Encoding.Unicode);

        var result = Installer(1603).Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsFailure);
        Assert.Contains("1603", result.Error, StringComparison.Ordinal);
        Assert.Contains("Return value 3", result.Error, StringComparison.Ordinal);
        Assert.Contains("***EnrollmentToken***", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_ExitCode1603_WithALockedLog_FailsWithoutThrowing()
    {
        // A log that another process holds open (no sharing) must not throw out of Install; the failure still returns.
        Directory.CreateDirectory(_paths.Logs);
        using var locked = new FileStream(
            Path.Combine(_paths.Logs, "msi-install.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        locked.Write(Encoding.Unicode.GetBytes("Error 1721 Return value 3\r\n"));
        locked.Flush();

        var result = Installer(1603).Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsFailure);
        Assert.Contains("1603", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_RunnerFailure_IsAFailureWithoutTheToken()
    {
        var installer = new MsiInstaller(_paths, _redactor, args =>
            Result<CommandOutput>.Failure($"msiexec.exe timed out after 300 s {string.Join(' ', args)}"));

        var result = installer.Install(MsiPath, Minted, _endpoint);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(Token, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_MsiOutsideTheRunFolder_IsRejectedBeforeMsiexec()
    {
        var outside = Path.Combine(_repo, "elsewhere", "agent.msi");

        var result = Installer(0).Install(outside, Minted, _endpoint);

        Assert.True(result.IsFailure);
        Assert.Empty(_calls);
    }

    [Fact]
    public void Uninstall_PassesTheExactArgumentList()
    {
        var log = Path.Combine(_paths.Logs, "msi-uninstall.log");

        var result = Installer(0).Uninstall(MsiPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(["/x", MsiPath, "/qn", "/norestart", "/l*v", log], _calls.Single());
    }

    [Fact]
    public void Uninstall_ExitCode3010_SucceedsWithRebootWarning()
    {
        var result = Installer(3010).Uninstall(MsiPath);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Warnings);
    }
}
