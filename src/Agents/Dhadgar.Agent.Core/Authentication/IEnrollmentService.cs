using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Interface for agent enrollment and certificate management.
/// </summary>
public interface IEnrollmentService
{
    /// <summary>
    /// Check if the agent is enrolled.
    /// </summary>
    bool IsEnrolled { get; }

    /// <summary>
    /// Enroll the agent with the control plane using a one-time token.
    /// </summary>
    /// <param name="enrollmentToken">One-time enrollment token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enrollment result with assigned node ID.</returns>
    Task<Result<EnrollmentResult>> EnrollAsync(
        string enrollmentToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew the agent certificate.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Renewal result.</returns>
    Task<Result<CertificateRenewalResult>> RenewCertificateAsync(
        CancellationToken cancellationToken = default);
}
