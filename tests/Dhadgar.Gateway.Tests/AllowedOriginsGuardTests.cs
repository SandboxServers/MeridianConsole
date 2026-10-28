using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// HF-02 (D-50): the credentialed origins in the Gateway and in compose are exactly the served frontends. Any
/// change to either list has to edit these tests on purpose.
/// </summary>
public sealed class AllowedOriginsGuardTests
{
    private static readonly string[] ServedFrontends =
    [
        "https://panel.meridianconsole.com",
        "https://meridianconsole.com",
        "https://dev.meridianconsole.com",
        "http://localhost:4321",
    ];

    private static readonly string[] ComposeFrontends =
    [
        "https://dev.meridianconsole.com",
        "https://panel.meridianconsole.com",
        "http://localhost:4321",
    ];

    [Fact]
    public void GatewayCorsOrigins_AreExactlyTheServedFrontends()
    {
        var root = RepoFiles.FindRepoRoot();
        var json = File.ReadAllText(Path.Combine(root, "src", "Dhadgar.Gateway", "appsettings.json"));

        // appsettings.json carries // comments, which the .NET configuration loader accepts.
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var document = JsonDocument.Parse(json, options);
        var origins = document.RootElement
            .GetProperty("Cors")
            .GetProperty("AllowedOrigins")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToList();

        AssertSameSet(ServedFrontends, origins, "Gateway Cors:AllowedOrigins in src/Dhadgar.Gateway/appsettings.json");
    }

    [Fact]
    public void ComposeTrustedOrigins_AreExactlyTheServedFrontends()
    {
        var root = RepoFiles.FindRepoRoot();
        var lines = File.ReadAllLines(Path.Combine(root, "deploy", "compose", "docker-compose.services.yml"));

        var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith("BETTER_AUTH_TRUSTED_ORIGINS:", StringComparison.Ordinal));
        Assert.True(line is not null, "BETTER_AUTH_TRUSTED_ORIGINS was not found in docker-compose.services.yml.");

        var value = line!.TrimStart()["BETTER_AUTH_TRUSTED_ORIGINS:".Length..];
        var origins = value.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToList();

        AssertSameSet(ComposeFrontends, origins, "BETTER_AUTH_TRUSTED_ORIGINS in deploy/compose/docker-compose.services.yml");
    }

    private static void AssertSameSet(string[] expected, List<string> actual, string source)
    {
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(
            extra.Count == 0 && missing.Count == 0,
            $"{source} differs from the served frontends. Extra: [{string.Join(", ", extra)}]. Missing: [{string.Join(", ", missing)}].");
    }
}
