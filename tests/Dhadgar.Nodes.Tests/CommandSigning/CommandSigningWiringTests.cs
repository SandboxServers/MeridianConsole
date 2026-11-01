using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dhadgar.Nodes.Tests.CommandSigning;

/// <summary>
/// Runs the real host wiring for <c>CommandSigning</c> options: the factory normally replaces <c>ICommandSigner</c>,
/// so these tests start the host with bad settings and assert that startup validation fails.
/// </summary>
public sealed class CommandSigningWiringTests
{
    [Fact]
    public void RelativeKeyPath_FailsHostStartup()
    {
        using var factory = new NodesWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(
            b => b.UseSetting("CommandSigning:KeyPath", "relative/key.pem"));

        var ex = Assert.ThrowsAny<Exception>(() => configured.CreateClient());

        Assert.Contains("CommandSigning:KeyPath", FlattenMessages(ex), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandTtlOverAgentLimit_FailsHostStartup()
    {
        using var factory = new NodesWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(
            b => b.UseSetting("CommandSigning:CommandTtlSeconds", "61"));

        var ex = Assert.ThrowsAny<Exception>(() => configured.CreateClient());

        Assert.Contains("CommandTtlSeconds", FlattenMessages(ex), StringComparison.Ordinal);
    }

    private static string FlattenMessages(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            parts.Add(current.Message);
        }

        return string.Join(" | ", parts);
    }
}
