using Dhadgar.Lab.Uat.Spec;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// Receives the progress of one run. The in-memory implementation is used until the bundle writer (L16) arrives.
/// </summary>
public interface IUatRunSink
{
    /// <summary>
    /// Called once, before the first row runs.
    /// </summary>
    /// <param name="runId">The six-character run id.</param>
    /// <param name="seed">The seed the rows run with.</param>
    /// <param name="spec">The spec being run.</param>
    void RunStarted(string runId, ulong seed, UatSpec spec);

    /// <summary>
    /// Called once per recorded row (PASS, FAIL, BLOCKED, SKIPPED and the rest), in file order.
    /// </summary>
    /// <param name="row">The graded row.</param>
    void RowFinished(UatRowResult row);

    /// <summary>
    /// Called once, after the last row and after the own lease (if any) is released.
    /// </summary>
    /// <param name="result">The run result.</param>
    void RunFinished(UatRunResult result);
}
