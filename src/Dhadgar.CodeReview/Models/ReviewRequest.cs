namespace Dhadgar.CodeReview.Models;

/// <summary>
/// Internal request model for triggering a code review.
/// </summary>
public class ReviewRequest
{
    public required string Owner { get; set; }
    public required string Repository { get; set; }
    public int PullRequestNumber { get; set; }
    public string? PullRequestTitle { get; set; }
    public string? PullRequestBody { get; set; }
    public string? TriggerSource { get; set; } // "webhook", "comment", "manual"
}

/// <summary>
/// Response from LLM containing review feedback.
/// </summary>
public class ReviewResponse
{
    public List<ReviewCommentDto> Comments { get; set; } = new();
    public string? Summary { get; set; }
}

/// <summary>
/// Individual code review comment from LLM.
/// </summary>
public class ReviewCommentDto
{
    public required string Path { get; set; }
    public int Line { get; set; }
    public required string Body { get; set; }
}

/// <summary>
/// Diff of a single file in a pull request.
/// </summary>
public class FileDiff
{
    public required string FileName { get; set; }
    public string? Patch { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public required string Status { get; set; } // added, modified, removed, renamed
}
