using System.Text.Json;
using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// D-ML106: the recovery judge reads the I5 entries of each array of an <c>invariants_check</c> payload. Pure, no processes, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryI5Tests
{
    [Fact]
    public void SkipPairAlone_IsAccepted()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[],"errors":[],"skipped":[
              {"invariant_id":"I5","source":"queues","reason":"stack not up (no lab credentials)"},
              {"invariant_id":"I5","source":"proxies","reason":"stack not up (no lab credentials)"}]}
            """);

        Assert.Empty(SelfCheckRecovery.I5Problems(payload.RootElement));
    }

    [Fact]
    public void SkipOfContainers_IsAProblem()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[],"errors":[],"skipped":[
              {"invariant_id":"I5","source":"containers","reason":"stack not up (no lab credentials)"}]}
            """);

        IReadOnlyList<string> problems = SelfCheckRecovery.I5Problems(payload.RootElement);

        Assert.Single(problems);
        Assert.Equal("I5 skipped containers after the restart: stack not up (no lab credentials)", problems[0]);
    }

    [Fact]
    public void QueueSkipWithAnotherReason_IsAProblem()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[],"errors":[],"skipped":[
              {"invariant_id":"I5","source":"queues","reason":"schema not created by its service yet"}]}
            """);

        IReadOnlyList<string> problems = SelfCheckRecovery.I5Problems(payload.RootElement);

        Assert.Single(problems);
        Assert.Equal("I5 skipped queues after the restart: schema not created by its service yet", problems[0]);
    }

    [Fact]
    public void I5Violation_NamesCheckIdAndService()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[{"invariant_id":"I5","service":"orphan-container","check_id":"meridian-lab-x","detail":"d"}],"errors":[],"skipped":[]}
            """);

        IReadOnlyList<string> problems = SelfCheckRecovery.I5Problems(payload.RootElement);

        Assert.Single(problems);
        Assert.Equal("I5 reports an orphan after the restart: meridian-lab-x orphan-container: d", problems[0]);
    }

    [Fact]
    public void I5Error_IsAProblem()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[],"errors":[{"invariant_id":"I5","error":"docker unreachable"}],"skipped":[]}
            """);

        IReadOnlyList<string> problems = SelfCheckRecovery.I5Problems(payload.RootElement);

        Assert.Single(problems);
        Assert.Equal("I5 could not be checked after the restart: docker unreachable", problems[0]);
    }

    [Fact]
    public void I2Violation_IsIgnored()
    {
        using JsonDocument payload = JsonDocument.Parse(
            """
            {"violations":[{"invariant_id":"I2","service":"nodes","check_id":"outbox-nodes","detail":"d"}],"errors":[],"skipped":[]}
            """);

        Assert.Empty(SelfCheckRecovery.I5Problems(payload.RootElement));
    }

    [Fact]
    public void NoViolationsArray_IsAProblem()
    {
        using JsonDocument payload = JsonDocument.Parse("""{"errors":[],"skipped":[]}""");

        IReadOnlyList<string> problems = SelfCheckRecovery.I5Problems(payload.RootElement);

        Assert.Single(problems);
        Assert.Equal("invariants_check returned no violations list", problems[0]);
    }
}
