using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Reads the enrollment token from <c>Agent:EnrollmentToken</c>. The value is read here rather than bound into
/// <see cref="Configuration.AgentOptions"/>, so options dumps never carry it.
/// </summary>
public sealed class ConfigurationEnrollmentTokenSource : IEnrollmentTokenSource
{
    /// <summary>Configuration key that holds the token.</summary>
    public const string ConfigurationKey = "Agent:EnrollmentToken";

    private readonly IConfiguration _configuration;

    /// <param name="configuration">Configuration that holds <see cref="ConfigurationKey"/>.</param>
    public ConfigurationEnrollmentTokenSource(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public Result<string> GetToken()
    {
        var value = _configuration[ConfigurationKey];
        return string.IsNullOrWhiteSpace(value)
            ? Result<string>.Success(string.Empty)
            : Result<string>.Success(value.Trim());
    }
}