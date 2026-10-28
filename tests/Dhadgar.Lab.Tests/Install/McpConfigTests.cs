using System.Text.Json;

namespace Dhadgar.Lab.Tests.Install;

/// <summary>
/// The root <c>.mcp.json</c> must point Claude Code at the daemon's loopback MCP endpoint (C16).
/// </summary>
public sealed class McpConfigTests
{
    [Fact]
    public void McpConfig_MeridianLabEntry_IsHttpType()
    {
        var entry = ReadMeridianLabEntry();

        Assert.Equal("http", entry.GetProperty("type").GetString());
    }

    [Fact]
    public void McpConfig_MeridianLabEntry_UrlMatchesDaemonDefaults()
    {
        var entry = ReadMeridianLabEntry();
        var url = new Uri(entry.GetProperty("url").GetString()!, UriKind.Absolute);

        Assert.Equal("http", url.Scheme);
        Assert.Equal("127.0.0.1", url.Host);
        Assert.Equal(LabDefaults.DaemonPort, url.Port);
        Assert.Equal(LabDefaults.McpPath, url.AbsolutePath);
    }

    [Fact]
    public void McpConfig_MeridianLabEntry_SendsBearerTokenFromEnvironment()
    {
        var entry = ReadMeridianLabEntry();
        var authorization = entry.GetProperty("headers").GetProperty("Authorization").GetString();

        Assert.Equal("Bearer ${" + LabDefaults.TokenEnvVar + "}", authorization);
    }

    [Fact]
    public void McpConfig_MeridianLabEntry_TimeoutIsThirtyMinutes()
    {
        var entry = ReadMeridianLabEntry();

        Assert.Equal(1_800_000, entry.GetProperty("timeout").GetInt32());
    }

    private static JsonElement ReadMeridianLabEntry()
    {
        var root = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".mcp.json")));

        return document.RootElement.GetProperty("mcpServers").GetProperty("meridian-lab").Clone();
    }
}
