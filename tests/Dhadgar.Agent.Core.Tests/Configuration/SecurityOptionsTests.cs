using System.ComponentModel.DataAnnotations;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Configuration;

public sealed class SecurityOptionsTests
{
    [Fact]
    public void Default_RenewalThreshold_IsProfileValue()
    {
        var options = new SecurityOptions();

        Assert.Equal(AgentCertificateProfile.RenewalThresholdDays, options.CertificateRenewalThresholdDays);
        Assert.Equal(30, options.CertificateRenewalThresholdDays);
    }

    [Fact]
    public void RenewalThreshold_AboveValidity_FailsValidation()
    {
        var options = new SecurityOptions { CertificateRenewalThresholdDays = AgentCertificateProfile.ValidityDays + 1 };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(SecurityOptions.CertificateRenewalThresholdDays)));

        // The boundary itself is valid, so the range is exactly 1..ValidityDays.
        var atBoundary = new SecurityOptions { CertificateRenewalThresholdDays = AgentCertificateProfile.ValidityDays };
        var valid90 = Validator.TryValidateObject(atBoundary, new ValidationContext(atBoundary), new List<ValidationResult>(), validateAllProperties: true);

        Assert.True(valid90);
    }
}