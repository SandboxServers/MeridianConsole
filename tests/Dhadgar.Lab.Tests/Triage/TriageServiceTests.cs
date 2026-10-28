using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Triage;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Triage;

/// <summary>
/// The triage buckets: a new signature of a run gets one finding, a known or already-found signature does not, and the ledger
/// renders one row per bucket with no raw pipe in a cell.
/// </summary>
public sealed class TriageServiceTests : IDisposable
{
    private const string RunId = "run-1";
    private const string SignatureId = "sig-0001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-triage-" + Path.GetRandomFileName());
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A WAL file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public void UnfoundedNewSignatures_NewSignatureWithoutFinding_IsOffered()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;
        SeedRunWithSignature(db, SignatureId, "new");

        IReadOnlyList<string> ids = db.UnfoundedNewSignaturesOfRun(RunId).Value;

        Assert.Single(ids);
        Assert.Equal(SignatureId, ids[0]);
    }

    [Fact]
    public void InsertFinding_TwiceKeepsOneFinding_AndNoOfferAfter()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;
        SeedRunWithSignature(db, SignatureId, "new");

        Assert.True(db.InsertFinding(SignatureId, RunId, "C:\\bundle\\repro\\repro.toml", "2026-10-28T00:00:00Z").IsSuccess);
        Assert.True(db.InsertFinding(SignatureId, RunId, null, "2026-10-29T00:00:00Z").IsSuccess);

        Assert.Empty(db.UnfoundedNewSignaturesOfRun(RunId).Value);
        ResultsDb.BucketRecord bucket = db.BucketOf(SignatureId).Value;
        Assert.Equal("new", bucket.FindingState);
        Assert.Equal("C:\\bundle\\repro\\repro.toml", bucket.ReproPath);
    }

    [Fact]
    public void SetSignatureKnownIssue_MovesSignatureToKnown_AndLeavesTheOffer()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;
        SeedRunWithSignature(db, SignatureId, "new");

        Assert.True(db.SetSignatureKnownIssue(SignatureId, "KI-DV13").IsSuccess);

        Assert.Empty(db.UnfoundedNewSignaturesOfRun(RunId).Value);
        ResultsDb.BucketRecord bucket = db.BucketOf(SignatureId).Value;
        Assert.Equal("known", bucket.State);
        Assert.Equal("KI-DV13", bucket.KnownIssueId);
    }

    [Fact]
    public void Draft_SetsDraftPathAndState()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;
        SeedRunWithSignature(db, SignatureId, "new");
        Assert.True(db.InsertFinding(SignatureId, RunId, null, "2026-10-28T00:00:00Z").IsSuccess);

        Assert.True(db.SetFindingDraft(SignatureId, "triage\\drafts\\sig-0001.md").IsSuccess);

        ResultsDb.BucketRecord bucket = db.BucketOf(SignatureId).Value;
        Assert.Equal("drafted", bucket.FindingState);
        Assert.Equal("triage\\drafts\\sig-0001.md", bucket.DraftPath);
    }

    [Fact]
    public void BucketOf_UnknownId_IsNotFound()
    {
        using ResultsDb db = ResultsDb.Open(_root, _validator).Value;

        Result<ResultsDb.BucketRecord> bucket = db.BucketOf("sig-missing");

        Assert.True(bucket.IsFailure);
        Assert.Contains("[Lab.NotFound]", bucket.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderLedger_OneRowPerBucket_EscapesPipes()
    {
        var buckets = new List<ResultsDb.BucketRecord>
        {
            new("sig-a", "row", "2026-10-28T00:00:00Z", "2026-10-28T01:00:00Z", 3, "new", null, null, "new",
                "C:\\repro|x.toml", null),
        };

        string text = TriageService.RenderLedger(buckets, "2026-10-28T02:00:00Z");

        Assert.Contains("| id | kind | state | hits |", text, StringComparison.Ordinal);
        Assert.Contains("| sig-a | row | new | 3 |", text, StringComparison.Ordinal);
        Assert.Contains("C:\\repro\\|x.toml", text, StringComparison.Ordinal);
    }

    private static void SeedRunWithSignature(ResultsDb db, string signatureId, string state)
    {
        Assert.True(db.UpsertRun(new RunRecord(RunId, "uat", null, 1, null, null, null, null, null, "fail", null, null,
            "C:\\bundle")).IsSuccess);
        Assert.True(db.InsertRow(new RowRecord(RunId, "catalog-nodes", "row-1", "fail", null, 1, null, signatureId,
            "[\"" + signatureId + "\"]", null, null, null)).IsSuccess);
        Assert.True(db.UpsertSignature(new SignatureRecord(signatureId, "row", "row fails", "2026-10-28T00:00:00Z", state,
            null)).IsSuccess);
        Assert.True(db.RecountSignatureHits(signatureId).IsSuccess);
    }
}
