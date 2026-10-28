using System.Runtime.Versioning;
using System.Text.Json;
using Dhadgar.Lab.Probe;

namespace Dhadgar.Lab.Tests.Probe;

/// <summary>
/// The footprint read against the real host. Read-only: it never reports a private key export, and the
/// serialised footprint carries no PEM block or certificate body.
/// </summary>
public sealed class FootprintReaderTests
{
    [SupportedOSPlatform("windows")]
    [SkippableFact]
    public void TryRead_OnHost_ReturnsMetadataOnly()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "footprint reads the Windows host");

        bool ok = FootprintReader.TryRead(out var footprint, out var error);

        Assert.True(ok, error);
        Assert.NotNull(footprint);

        var json = JsonSerializer.Serialize(footprint);
        Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN CERTIFICATE", json, StringComparison.Ordinal);
    }
}
