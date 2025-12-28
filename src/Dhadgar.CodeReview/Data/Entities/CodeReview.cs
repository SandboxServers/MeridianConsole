namespace Dhadgar.CodeReview.Data.Entities;

/// <summary>
/// Represents a code review performed by the LLM on a pull request.
/// </summary>
public class CodeReview
{
    public int Id { get; set; }

    /// <summary>
    /// GitHub repository full name (e.g., "owner/repo")
    /// </summary>
    public required string Repository { get; set; }

    /// <summary>
    /// Pull request number
    /// </summary>
    public int PullRequestNumber { get; set; }

    /// <summary>
    /// GitHub review ID (after posting to GitHub)
    /// </summary>
    public long? GitHubReviewId { get; set; }

    /// <summary>
    /// Overall summary of the review
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Timestamp when review was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when review was completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Status of the review (Pending, InProgress, Completed, Failed)
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Error message if review failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Model used for the review (e.g., "deepseek-coder:33b")
    /// </summary>
    public required string ModelUsed { get; set; }

    /// <summary>
    /// Time taken to generate the review in seconds
    /// </summary>
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// Individual comments on the PR
    /// </summary>
    public ICollection<ReviewComment> Comments { get; set; } = new List<ReviewComment>();
}
