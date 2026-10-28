using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Secrets;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I4 over raw lines: a secret in a raw line is a violation, the stored detail and evidence are redacted, and clean lines are silent.
/// </summary>
public sealed class I4SecretLeakInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0.AddMinutes(-5), T0, null, null);

    [Fact]
    public async Task I4_PlantedVaultValue_ViolationIsRedacted()
    {
        const string planted = "s3cr3t-planted-value-0123";
        using var vault = new SecretVault();
        string reference = vault.Add(SecretKind.Password, planted.AsSpan());
        var check = new I4SecretLeakInvariant(vault, new Redactor(vault));

        check.Observe("nodes", $"boot: value={planted} done".AsSpan());
        IReadOnlyList<InvariantViolation> violations = (await check.CheckAsync(Window, CancellationToken.None)).Value!;

        InvariantViolation violation = Assert.Single(violations);
        Assert.Equal("I4", violation.InvariantId);
        Assert.Equal("nodes", violation.Service);
        Assert.Equal($"vault-value:{reference}", violation.CheckId);
        Assert.DoesNotContain(planted, violation.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(planted, violation.Evidence!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Contains($"***{reference}", violation.Evidence.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task I4_CleanLines_NoViolation()
    {
        using var vault = new SecretVault();
        var check = new I4SecretLeakInvariant(vault, new Redactor(vault));

        check.Observe("nodes", "GET /api/v1/nodes 200".AsSpan());
        check.Observe("nodes", "Password=".AsSpan());

        IReadOnlyList<InvariantViolation> violations = (await check.CheckAsync(Window, CancellationToken.None)).Value!;
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", "pem-private-key")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123", "bearer-token")]
    [InlineData("conn Host=db;Password=hunter2", "password-value")]
    public async Task I4_SecretPatterns_Violation(string line, string checkId)
    {
        using var vault = new SecretVault();
        var check = new I4SecretLeakInvariant(vault, new Redactor(vault));

        check.Observe("gateway", line.AsSpan());

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal(checkId, violation.CheckId);
        Assert.DoesNotContain("hunter2", violation.Evidence!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task I4_DaemonTokenInLine_Violation()
    {
        using var vault = new SecretVault();
        var check = new I4SecretLeakInvariant(vault, new Redactor(vault), line => line.Contains("DAEMON-TOKEN-VALUE", StringComparison.Ordinal));

        check.Observe("nodes", "token DAEMON-TOKEN-VALUE leaked".AsSpan());

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("daemon-token", violation.CheckId);
    }

    [Fact]
    public async Task I4_CheckDrainsQueue_SecondCheckIsEmpty()
    {
        using var vault = new SecretVault();
        var check = new I4SecretLeakInvariant(vault, new Redactor(vault));

        check.Observe("nodes", "-----BEGIN PRIVATE KEY-----".AsSpan());

        Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Empty((await check.CheckAsync(Window, CancellationToken.None)).Value!);
    }
}
