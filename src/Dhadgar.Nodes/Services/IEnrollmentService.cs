using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Services;

public interface IEnrollmentService
{
    /// <summary>
    /// Enrolls a new agent using the provided enrollment token.
    /// Creates the node, validates the token, and returns enrollment credentials.
    /// </summary>
    Task<ServiceResult<EnrollNodeResponse>> EnrollAsync(
        EnrollNodeRequest request,
        CancellationToken ct = default);
}
