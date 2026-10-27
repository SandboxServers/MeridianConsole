using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Scenarios;

/// <summary>Records a failed scenario step in the evidence map and returns the failure that names it.</summary>
internal static class StepFailure
{
    /// <summary>Marks <paramref name="step"/> as failed in <paramref name="evidence"/> and returns a failure with <paramref name="message"/>.</summary>
    internal static Result Fail(Dictionary<string, object?> evidence, string step, string message)
    {
        evidence[step] = "fail";
        return Result.Failure($"{step}: {message}");
    }
}