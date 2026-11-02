using Dhadgar.Lab.Processes;

namespace Dhadgar.Lab.Tests.Services;

public sealed class ServiceLogTailTests
{
    [Fact]
    public void Write_RedactsCredentialPairs()
    {
        var line = new LogLine(
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero),
            "Information",
            "Dhadgar.Identity",
            null,
            "token issued access_token=abc for client");

        string formatted = ServiceLogTail.Format("identity", line);

        Assert.Contains("access_token=[redacted]", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=abc", formatted, StringComparison.Ordinal);
        Assert.StartsWith("identity-log 2026-11-02T00:00:00.0000000+00:00 Information Dhadgar.Identity ", formatted, StringComparison.Ordinal);
    }
}
