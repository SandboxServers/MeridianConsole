using System.ComponentModel.DataAnnotations;
using Dhadgar.Agent.Core.Configuration;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Configuration;

public sealed class AgentOptionsValidationTests
{
    private static List<ValidationResult> Validate(AgentOptions options) =>
        options.Validate(new ValidationContext(options)).ToList();

    [Fact]
    public void NodeIdWithoutOrganization_Fails()
    {
        var results = Validate(new AgentOptions { NodeId = Guid.NewGuid() });

        var result = Assert.Single(results);
        Assert.Equal(["NodeId", "OrganizationId"], result.MemberNames);
    }

    [Fact]
    public void OrganizationWithoutNodeId_Fails()
    {
        var results = Validate(new AgentOptions { OrganizationId = Guid.NewGuid() });

        var result = Assert.Single(results);
        Assert.Equal(["NodeId", "OrganizationId"], result.MemberNames);
    }

    [Fact]
    public void BothIdsWithDefaultSecurity_Passes()
    {
        var results = Validate(new AgentOptions
        {
            NodeId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Security = new SecurityOptions(),
        });

        Assert.Empty(results);
    }

    [Fact]
    public void NeitherId_Passes()
    {
        Assert.Empty(Validate(new AgentOptions()));
    }

    [Fact]
    public void RelativeStateDirectory_Fails()
    {
        var results = Validate(new AgentOptions { StateDirectory = "relative/dir" });

        var result = Assert.Single(results);
        Assert.Equal(["StateDirectory"], result.MemberNames);
    }

    [Fact]
    public void FullyQualifiedStateDirectory_Passes()
    {
        var results = Validate(new AgentOptions { StateDirectory = Path.Combine(Path.GetTempPath(), "x") });

        Assert.Empty(results);
    }

    [Fact]
    public void NestedRenewalRetryOutOfRange_FailsTryValidateObject()
    {
        var options = new AgentOptions();
        options.ControlPlane.RenewalRetryMinutes = 31;

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), new List<ValidationResult>(), validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void NestedConnectionCheckIntervalZero_FailsTryValidateObject()
    {
        var options = new AgentOptions();
        options.ControlPlane.ConnectionCheckIntervalSeconds = 0;

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), new List<ValidationResult>(), validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void NestedRenewalThresholdAboveValidity_FailsTryValidateObject()
    {
        var options = new AgentOptions();
        options.Security.CertificateRenewalThresholdDays = 91;

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), new List<ValidationResult>(), validateAllProperties: true);

        Assert.False(valid);
    }

    [Theory]
    [InlineData(@"\foo")]
    [InlineData("C:foo")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"\\?\C:\x")]
    [InlineData("//server/share/x")]
    [InlineData("PARENT")]
    public void StateDirectory_NotLocalOrNotQualified_Fails(string directory)
    {
        if (directory == "PARENT")
        {
            directory = Path.Combine(Path.GetTempPath(), "..", "x");
        }

        var results = Validate(new AgentOptions { StateDirectory = directory });

        var result = Assert.Single(results);
        Assert.Contains("StateDirectory", result.MemberNames);
    }
}
