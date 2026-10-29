using System.Text;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools.Fuzz;

namespace Dhadgar.Lab.Tests.Fuzz;

/// <summary>
/// The fuzz leak predicate (R2-N9, S-N3): a registered vault value and the daemon token are found in a body, and a clean body is
/// not. The test builds the vault and the edge state the way the I4 and secret scanner tests do.
/// </summary>
public sealed class FuzzRunToolLeakCheckTests
{
    private const string VaultValue = "vault-value-7f3a";
    private const string DaemonToken = "daemon-token-0123456789abcdef";

    [Fact]
    public void LeakCheck_RegisteredVaultValue_IsFound()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.BearerToken, VaultValue.AsSpan());
        using var edge = new EdgeState(Encoding.ASCII.GetBytes(DaemonToken));

        Func<string, bool> leakCheck = FuzzRunTool.LeakCheck(vault, edge);

        Assert.True(leakCheck("{\"detail\":\"" + VaultValue + "\"}"));
    }

    [Fact]
    public void LeakCheck_DaemonTokenBytes_AreFound()
    {
        using var vault = new SecretVault();
        using var edge = new EdgeState(Encoding.ASCII.GetBytes(DaemonToken));

        Func<string, bool> leakCheck = FuzzRunTool.LeakCheck(vault, edge);

        Assert.True(leakCheck("{\"detail\":\"echo " + DaemonToken + " back\"}"));
    }

    [Fact]
    public void LeakCheck_CleanBody_IsNotFound()
    {
        using var vault = new SecretVault();
        vault.Add(SecretKind.BearerToken, VaultValue.AsSpan());
        using var edge = new EdgeState(Encoding.ASCII.GetBytes(DaemonToken));

        Func<string, bool> leakCheck = FuzzRunTool.LeakCheck(vault, edge);

        Assert.False(leakCheck("{\"type\":\"t\",\"title\":\"Bad Request\",\"status\":400}"));
    }
}
