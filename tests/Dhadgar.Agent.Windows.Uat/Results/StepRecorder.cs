using System.Diagnostics;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>
/// Runs steps in order, times each one, and maps its <see cref="Result"/> or a caught exception to a status.
/// After the first <c>fail</c>, every later step records <c>skipped</c>. Cleanup is not a step and always runs.
/// </summary>
internal sealed class StepRecorder
{
    private readonly List<StepResult> _steps = [];
    private bool _failed;

    /// <summary>The steps recorded so far, in order.</summary>
    public IReadOnlyList<StepResult> Steps => _steps;

    /// <summary>Runs one step.</summary>
    public async Task<StepResult> RunAsync(
        string id,
        string name,
        Func<CancellationToken, Task<Result<Dictionary<string, object?>>>> body,
        CancellationToken ct)
    {
        if (_failed)
        {
            return Add(Skip(id, name, "skipped after an earlier failure"));
        }

        var clock = Stopwatch.StartNew();
        StepResult step;
        try
        {
            var outcome = await body(ct);
            step = outcome.IsSuccess
                ? Make(id, name, "pass", outcome.Value, null, clock)
                : Make(id, name, "fail", null, outcome.Error, clock);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            step = Make(id, name, "inconclusive", null, "cancelled", clock);
        }
#pragma warning disable CA1031 // A step boundary turns any exception into a recorded failure (R-RESULT).
        catch (Exception ex)
        {
            step = Make(id, name, "fail", null, $"{ex.GetType().FullName}: {ex.Message}", clock);
        }
#pragma warning restore CA1031

        if (step.Status == "fail")
        {
            _failed = true;
        }

        return Add(step);
    }

    /// <summary>Records a step that did not run.</summary>
    public StepResult Skip(string id, string name, string message) =>
        new(id, name, "skipped", new Dictionary<string, object?>(), message, 0);

    /// <summary>Records a step that did not run and adds it to the list.</summary>
    public StepResult AddSkipped(string id, string name, string message) => Add(Skip(id, name, message));

    private StepResult Add(StepResult step)
    {
        _steps.Add(step);
        return step;
    }

    private static StepResult Make(
        string id,
        string name,
        string status,
        Dictionary<string, object?>? evidence,
        string? message,
        Stopwatch clock)
    {
        clock.Stop();
        return new StepResult(
            id,
            name,
            status,
            evidence ?? new Dictionary<string, object?>(),
            message,
            (long)clock.Elapsed.TotalMilliseconds);
    }
}
