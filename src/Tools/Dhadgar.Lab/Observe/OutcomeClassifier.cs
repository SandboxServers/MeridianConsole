using System.Net.Http;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// Maps what happened to one request to an <see cref="OpOutcome"/> (C18 outcome table). A response is <c>Ok</c> or
/// <c>Fail</c> by the op's success statuses. A refused connection, a name or TLS failure is <c>Fail</c>, because nothing
/// was sent. Any other failure after the request began is <c>Unknown</c>: the request may have been applied.
/// </summary>
public static class OutcomeClassifier
{
    /// <summary>
    /// Classifies one attempt. <paramref name="successStatuses"/> defaults to every 2xx status.
    /// </summary>
    /// <param name="response">The response that was read, or null when none was.</param>
    /// <param name="error">The failure, or null when the call returned a response.</param>
    /// <param name="successStatuses">The statuses the op defines as success.</param>
    public static OpOutcome Classify(HttpResponseMessage? response, Exception? error, IReadOnlySet<int>? successStatuses = null)
    {
        if (response is not null)
        {
            int status = (int)response.StatusCode;
            bool success = successStatuses is null ? status is >= 200 and < 300 : successStatuses.Contains(status);
            return success ? OpOutcome.Ok : OpOutcome.Fail;
        }

        if (error is HttpRequestException httpError && httpError.HttpRequestError is
            HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError)
        {
            return OpOutcome.Fail;
        }

        return OpOutcome.Unknown;
    }
}
