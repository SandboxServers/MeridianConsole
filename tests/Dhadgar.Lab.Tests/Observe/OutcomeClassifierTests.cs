using System.Net;
using System.Net.Http;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// The outcome table: a response is judged by status, a refused connection is a failure, and anything after the request
/// began is unknown.
/// </summary>
public sealed class OutcomeClassifierTests
{
    [Fact]
    public void ConnectionError_IsFail()
    {
        var error = new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        Assert.Equal(OpOutcome.Fail, OutcomeClassifier.Classify(null, error));
    }

    [Fact]
    public void ResponseEnded_IsUnknown()
    {
        var error = new HttpRequestException(HttpRequestError.ResponseEnded, "ended early");

        Assert.Equal(OpOutcome.Unknown, OutcomeClassifier.Classify(null, error));
    }

    [Fact]
    public void Cancelled_IsUnknown()
    {
        Assert.Equal(OpOutcome.Unknown, OutcomeClassifier.Classify(null, new TaskCanceledException("cancelled")));
        Assert.Equal(OpOutcome.Unknown, OutcomeClassifier.Classify(null, new OperationCanceledException()));
    }

    [Fact]
    public void Status401_IsFail()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        Assert.Equal(OpOutcome.Fail, OutcomeClassifier.Classify(response, null));
    }

    [Fact]
    public void Status201_IsOk()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created);

        Assert.Equal(OpOutcome.Ok, OutcomeClassifier.Classify(response, null));
    }

    [Fact]
    public void Status409_WithSuccessSet_IsOk()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);

        Assert.Equal(OpOutcome.Ok, OutcomeClassifier.Classify(response, null, new HashSet<int> { 409 }));
    }
}
