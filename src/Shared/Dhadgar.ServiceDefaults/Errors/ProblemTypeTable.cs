namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// D-ML233: the one problem-type table for client errors. Shared by <see cref="BindingFailureProblem"/> and
/// <see cref="StatusCodeProblemExtensions.UseDhadgarStatusCodeProblems"/> so both name a status the same way.
/// </summary>
public static class ProblemTypeTable
{
    public const string BadRequestType = "https://meridian.console/errors/bad-request";

    /// <summary>The type URI and title for a 4xx status. Unknown 4xx statuses map to bad-request.</summary>
    public static (string Type, string Title) Describe(int statusCode) => statusCode switch
    {
        400 => (BadRequestType, "Bad Request"),
        405 => ("https://meridian.console/errors/method-not-allowed", "Method Not Allowed"),
        408 => ("https://meridian.console/errors/request-timeout", "Request Timeout"),
        413 => ("https://meridian.console/errors/payload-too-large", "Payload Too Large"),
        415 => ("https://meridian.console/errors/unsupported-media-type", "Unsupported Media Type"),
        431 => ("https://meridian.console/errors/request-header-fields-too-large", "Request Header Fields Too Large"),
        _ => (BadRequestType, "Bad Request"),
    };
}
