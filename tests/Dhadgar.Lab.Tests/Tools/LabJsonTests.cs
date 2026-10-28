using System.Text.Json;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools;

namespace Dhadgar.Lab.Tests.Tools;

/// <summary>
/// The shared tool JSON rules: enums go out as snake_case strings, so an MCP client can read them.
/// </summary>
public sealed class LabJsonTests
{
    [Fact]
    public void LeaseHistoryEntry_TakenOverReason_SerializesAsSnakeCaseString()
    {
        var at = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        var entry = new LeaseHistoryEntry("session-a", "chaos", at, at, LeaseEndReason.TakenOver, "session-b", "takeover");

        var json = JsonSerializer.Serialize(entry, LabJson.Options);

        Assert.Contains("\"reason\":\"taken_over\"", json, StringComparison.Ordinal);
    }
}
