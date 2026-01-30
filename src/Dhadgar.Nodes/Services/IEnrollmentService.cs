using Dhadgar.Nodes.Models;

// Alias local models to avoid ambiguity with Contracts types
using LocalEnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using LocalEnrollNodeResponse = Dhadgar.Nodes.Models.EnrollNodeResponse;

namespace Dhadgar.Nodes.Services;

public interface IEnrollmentService
{
    /// <summary>
    /// Enrolls a new agent using the provided enrollment token.
    /// Creates the node, validates the token, and returns enrollment credentials.
    /// </summary>
    Task<ServiceResult<LocalEnrollNodeResponse>> EnrollAsync(
        LocalEnrollNodeRequest request,
        CancellationToken ct = default);
}
