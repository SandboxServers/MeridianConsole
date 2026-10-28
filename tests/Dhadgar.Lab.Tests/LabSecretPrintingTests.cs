using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Tools;

namespace Dhadgar.Lab.Tests;

/// <summary>
/// Secret-bearing records must not print their secret through the compiler-generated <c>ToString</c>.
/// </summary>
public sealed class LabSecretPrintingTests
{
    [Fact]
    public void LabDaemonOptions_ToString_DoesNotContainToken()
    {
        var options = new LabDaemonOptions(Port: 8789, Token: "s3cr3t-token-value", DataRoot: "C:\\lab");

        Assert.DoesNotContain("s3cr3t-token-value", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LabCallContext_ToString_DoesNotContainLeaseId()
    {
        var ctx = new LabCallContext("c1", "lease-secret-abc", "mcp");

        Assert.DoesNotContain("lease-secret-abc", ctx.ToString(), StringComparison.Ordinal);
    }
}
