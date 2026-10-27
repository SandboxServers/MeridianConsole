using System.ComponentModel.DataAnnotations;
using Dhadgar.Agent.Core.Configuration;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Configuration;

public sealed class ControlPlaneOptionsRenewalTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void RenewalRetryMinutes_OutsideOneToThirty_FailsValidation(int minutes, bool expectedValid)
    {
        // Retries must stay inside Nodes' grace window (D-S212), so the range is 1..30 minutes.
        var options = new ControlPlaneOptions
        {
            Endpoint = "https://localhost",
            RenewalRetryMinutes = minutes,
        };

        var errors = options.Validate(new ValidationContext(options)).ToList();

        var namesRetry = errors.Any(e => e.MemberNames.Contains(nameof(ControlPlaneOptions.RenewalRetryMinutes)));
        Assert.Equal(expectedValid, !namesRetry);
    }
}
