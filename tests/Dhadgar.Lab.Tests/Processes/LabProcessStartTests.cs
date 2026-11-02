using System.Diagnostics;
using Dhadgar.Lab;
using Dhadgar.Lab.Processes;
using Xunit;

namespace Dhadgar.Lab.Tests.Processes;

/// <summary>
/// The one child-process helper removes the lab token from every child's environment, whatever the key's case (H3).
/// </summary>
public sealed class LabProcessStartTests
{
    [Fact]
    public void StripToken_RemovesExplicitTokenKey()
    {
        var info = new ProcessStartInfo("cmd");
        info.Environment[LabDefaults.TokenEnvVar.ToLowerInvariant()] = "lab-token-must-not-reach-a-child";
        info.Environment["LAB_KEEP_ME"] = "kept";

        LabProcessStart.StripToken(info);

        Assert.DoesNotContain(info.Environment.Keys,
            key => string.Equals(key, LabDefaults.TokenEnvVar, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("kept", info.Environment["LAB_KEEP_ME"]);
    }
}
