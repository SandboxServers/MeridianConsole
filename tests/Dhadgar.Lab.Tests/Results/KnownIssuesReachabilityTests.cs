using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// Every shipped known issue has a sample failure that <see cref="SignatureBuilder"/> can produce and that
/// <see cref="KnownIssues.Match"/> links to that issue (D-ML96). An entry with no reachable sample is documentation, not a seed.
/// </summary>
public sealed class KnownIssuesReachabilityTests
{
    private static readonly Dictionary<string, RowFailure> Samples = new(StringComparer.Ordinal)
    {
        ["KI-ENROLL-RETRY"] = new("history", "enrollment", null,
            [SignatureBuilder.History("enrollment", "E5", "enroll-lost-response")]),
        ["KI-F67"] = new("catalog-nodes", "nodes-pg-down-enroll", null,
        [
            SignatureBuilder.Clause("catalog-nodes", "nodes-pg-down-enroll", "step:enroll-down", "NotHeld"),
            SignatureBuilder.Invariant("I1", "nodes", "unhandled"),
            SignatureBuilder.Http(new HttpEvidence("nodes", 500, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", [])),
        ]),
        ["KI-F69"] = new("catalog-nodes", "nodes-enroll-malformed", null,
            [SignatureBuilder.Clause("catalog-nodes", "nodes-enroll-malformed", "problem-json", "NotHeld")]),
        ["KI-F69-HTTP"] = new("fuzz", "enroll/1", null,
            [SignatureBuilder.Http(new HttpEvidence("nodes", 400, null, "POST", "/api/v1/agents/enroll", "/api/v1/agents/enroll", []))]),
        ["KI-NOTIF-OUTBOX"] = new("catalog-notifications", "outbox", null, [SignatureBuilder.Invariant("I2", "notifications", "outbox")]),
        ["KI-RABBIT-HOST"] = new("catalog-notifications", "broker", null, [SignatureBuilder.Exception(new ExceptionEvidence(
            "notifications", "RabbitMQ.Client.Exceptions.BrokerUnreachableException", ["at RabbitMQ.Client.ConnectionFactory.CreateConnection()"]))]),
        ["KI-DISCORD-SECRETS-PORT"] = new("catalog-discord", "secrets", null, [SignatureBuilder.Exception(new ExceptionEvidence(
            "discord", "System.Net.Http.HttpRequestException", ["at System.Net.Http.HttpConnectionPool.ConnectAsync()"]))]),
    };

    [Fact]
    public void EveryShippedIssue_HasAReachableSample()
    {
        KnownIssues shipped = KnownIssues.Load(ShippedPath()).Value!;

        foreach (KnownIssue issue in shipped.Issues)
        {
            Assert.True(Samples.ContainsKey(issue.Id), issue.Id + " has no reachability sample (D-ML96)");
        }
    }

    [Fact]
    public void EverySample_MatchesItsIssue()
    {
        KnownIssues shipped = KnownIssues.Load(ShippedPath()).Value!;

        foreach (KeyValuePair<string, RowFailure> sample in Samples)
        {
            KnownMatch match = shipped.Match(sample.Value);

            Assert.Equal(sample.Key, match.IssueId);
            Assert.NotEqual("new", match.State);
            Assert.NotEqual("claimed_mismatch", match.State);
        }
    }

    private static string ShippedPath() => Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
        "docs", "guides", "uat-specs", "known-issues.toml");
}
