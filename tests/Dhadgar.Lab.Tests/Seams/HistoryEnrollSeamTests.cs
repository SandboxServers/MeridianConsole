using System.Globalization;
using Dhadgar.Lab;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Nodes;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tests.History;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L2h: the lab's history client sends the product wire shape and the in-process Nodes app accepts it. A lab-minted token
/// gives 201 and one node; the same token sent again gives 401, which shows the token step was reached (D-ML117, F103).
/// </summary>
public sealed class HistoryEnrollSeamTests
{
    private const string RunId = "9f2c4a1e-7b00-4c11-8d22-000000000077";

    [Fact]
    [Trait("Seam", "S-L2h")]
    public async Task Enroll_HistoryClient_CreatedThenUnauthorizedOnReplay()
    {
        using var factory = new LabNodesFactory();
        using var vault = new SecretVault();
        var adapter = new NodesLabAdapter(factory.CreateDbContext, vault, TimeProvider.System, NullLoggerFactory.Instance);

        var minted = await adapter.MintTokenAsync(Guid.NewGuid(), "lab-s2h", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.True(minted.IsSuccess, minted.Error);

        using var http = factory.CreateClient();
        // The in-memory TestServer ignores the host, so the lab loopback endpoint passes HistoryClient's allowlist check.
        var endpoint = new Uri("http://127.0.0.1:" + LabPorts.Barrier.ToString(CultureInfo.InvariantCulture) + "/agents/enroll");
        var redactor = new Redactor(vault);

        var first = new FakeObservationSink();
        using (var client = new HistoryClient(RunId, 1, "key-a", "linux", minted.Value.TokenRef, endpoint, http, first, vault, redactor, TimeProvider.System))
        {
            Result<HistoryClientOutcome> outcome = await client.RunAsync(HistoryRetry.None, CancellationToken.None);
            Assert.True(outcome.IsSuccess, outcome.Error);
        }

        Assert.Equal(201, CompletedStatus(first));

        using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, db.Nodes.Count());
        }

        var second = new FakeObservationSink();
        using (var client = new HistoryClient(RunId, 2, "key-b", "linux", minted.Value.TokenRef, endpoint, http, second, vault, redactor, TimeProvider.System))
        {
            _ = await client.RunAsync(HistoryRetry.None, CancellationToken.None);
        }

        Assert.Equal(401, CompletedStatus(second));
    }

    /// <summary>Reads the status of the single Complete line the client wrote.</summary>
    private static int CompletedStatus(FakeObservationSink sink)
    {
        LabObservation complete = Assert.Single(sink.Lines, line => line.Kind == ObservationKind.Complete);
        return complete.Result.GetProperty("status").GetInt32();
    }
}
