using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Services;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Services;

public sealed class LabServicesHealthTests
{
    // The exact shape WriteHealthResponseAsync writes (ServiceDefaultsExtensions.cs), with a credential in the failing check's description.
    private const string ServiceDefaultsBody =
        """{"service":"Dhadgar.Notifications","status":"unhealthy","timestamp":"2026-11-02T00:00:00Z","checks":{"self":{"status":"Healthy","duration_ms":0.1},"masstransit-bus":{"status":"Unhealthy","duration_ms":3.2,"description":"Not ready: connect failed amqp://lab:hunter2@x"}}}""";

    [Fact]
    public void FailingHealthChecks_ServiceDefaultsBody_NamesUnhealthyChecksOnly()
    {
        string names = LabServices.FailingHealthChecks(ServiceDefaultsBody);

        Assert.Equal("masstransit-bus", names);
        Assert.DoesNotContain("hunter2", names, StringComparison.Ordinal);
        Assert.DoesNotContain("description", names, StringComparison.Ordinal);
    }

    [Fact]
    public void FailingHealthChecks_NonJsonBody_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LabServices.FailingHealthChecks("<html>502 Bad Gateway</html>"));
    }

    [Fact]
    public void ReadinessTimeoutMessage_WithoutChecks_NamesServiceStatusAndContainer()
    {
        string message = LabServices.ReadinessTimeoutMessage("nodes", "HTTP 503", string.Empty, "running 0");

        Assert.Equal(
            $"[Lab.Timeout] nodes did not answer 200 within {LabServices.ReadinessTimeoutSeconds} s; last status HTTP 503; container running 0",
            message);
    }

    [Fact]
    public void ReadinessTimeoutMessage_WithChecks_NamesTheFailingChecks()
    {
        string message = LabServices.ReadinessTimeoutMessage("notifications", "HTTP 503", "masstransit-bus", "exited 139");

        Assert.Equal(
            $"[Lab.Timeout] notifications did not answer 200 within {LabServices.ReadinessTimeoutSeconds} s; last status HTTP 503; "
            + "failing checks: masstransit-bus; container exited 139",
            message);
        Assert.Contains("did not answer 200 within", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessTimeoutMessage_NonDefaultReadyStatus_NamesThatStatus()
    {
        string message = LabServices.ReadinessTimeoutMessage("discord", "HTTP 200", string.Empty, "running 0", 503);

        Assert.Contains("did not answer 503 within", message, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer 200", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseContainerState_StatusAndExitCode_IsKept()
    {
        Assert.Equal("exited 139", LabServices.ParseContainerState(Inspect(0, "exited 139\n")));
        Assert.Equal("running 0", LabServices.ParseContainerState(Inspect(0, "running 0")));
    }

    [Fact]
    public void ParseContainerState_DockerErrorText_BecomesUnknown()
    {
        Assert.Equal("unknown", LabServices.ParseContainerState(Inspect(1, "Error: no such object")));
        Assert.Equal("unknown", LabServices.ParseContainerState(Inspect(0, "Error: no such object")));
        Assert.Equal("unknown", LabServices.ParseContainerState(Result<DockerOutput>.Failure("[Lab.Docker] docker is not running")));
        Assert.Equal("unknown", LabServices.ParseContainerState(Inspect(0, "Running 0")));
        Assert.Equal("unknown", LabServices.ParseContainerState(Inspect(0, "exited 139 password=hunter2")));
    }

    private static Result<DockerOutput> Inspect(int exitCode, string stdout) =>
        Result<DockerOutput>.Success(new DockerOutput(exitCode, stdout, string.Empty));
}
