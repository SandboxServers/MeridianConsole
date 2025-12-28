namespace Dhadgar.CodeReview.Data.Entities;

/// <summary>
/// Represents an individual comment made during a code review.
/// </summary>
public class ReviewComment
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the parent CodeReview
    /// </summary>
    public int CodeReviewId { get; set; }

    /// <summary>
    /// Navigation property to parent review
    /// </summary>
    public CodeReview? CodeReview { get; set; }

    /// <summary>
    /// File path relative to repository root
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Line number in the file
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// The comment body/text
    /// </summary>
    public required string Body { get; set; }

    /// <summary>
    /// GitHub comment ID (after posting)
    /// </summary>
    public long? GitHubCommentId { get; set; }

    /// <summary>
    /// Timestamp when comment was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
