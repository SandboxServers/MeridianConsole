using Dhadgar.CodeReview.Data;
using Dhadgar.CodeReview.Data.Entities;
using Dhadgar.CodeReview.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Octokit;
using System.Diagnostics;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Orchestrates the code review process by coordinating GitHub and Ollama services.
/// </summary>
public class ReviewOrchestrator
{
    private readonly GitHubService _gitHubService;
    private readonly OllamaService _ollamaService;
    private readonly CouncilService _councilService;
    private readonly CodeReviewDbContext _dbContext;
    private readonly ReviewOptions _options;
    private readonly ILogger<ReviewOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ReviewOrchestrator(
        GitHubService gitHubService,
        OllamaService ollamaService,
        CouncilService councilService,
        CodeReviewDbContext dbContext,
        IOptions<ReviewOptions> options,
        ILogger<ReviewOrchestrator> logger,
        ILoggerFactory loggerFactory)
    {
        _gitHubService = gitHubService;
        _ollamaService = ollamaService;
        _councilService = councilService;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Perform a code review on a pull request.
    /// </summary>
    public async Task<Data.Entities.CodeReview> PerformReviewAsync(
        ReviewRequest request,
        CancellationToken cancellationToken = default,
        long? statusCommentId = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var repository = $"{request.Owner}/{request.Repository}";

        _logger.LogInformation(
            "Starting code review for PR #{Number} in {Repo} (triggered by {Source})",
            request.PullRequestNumber,
            repository,
            request.TriggerSource);

        // Create database record
        var review = new Data.Entities.CodeReview
        {
            Repository = repository,
            PullRequestNumber = request.PullRequestNumber,
            Status = "Pending",
            ModelUsed = _options.Model ?? "unknown",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CodeReviews.Add(review);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // Update status to in progress
            review.Status = "InProgress";
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Step 1: Fetch PR diff from GitHub
            _logger.LogInformation("Fetching PR diff from GitHub...");
            var diffs = await _gitHubService.GetPullRequestDiffAsync(
                request.Owner,
                request.Repository,
                request.PullRequestNumber,
                cancellationToken);

            // Validate diff size
            var totalChanges = diffs.Sum(d => d.Additions + d.Deletions);
            if (totalChanges > _options.MaxDiffSize)
            {
                throw new InvalidOperationException(
                    $"PR is too large ({totalChanges} changes). Maximum allowed: {_options.MaxDiffSize}");
            }

            if (diffs.Count > _options.MaxFilesPerReview)
            {
                throw new InvalidOperationException(
                    $"Too many files changed ({diffs.Count}). Maximum allowed: {_options.MaxFilesPerReview}");
            }

            if (diffs.Count == 0)
            {
                _logger.LogWarning("No files changed in PR #{Number}", request.PullRequestNumber);
                review.Status = "Completed";
                review.Summary = "No files changed in this pull request.";
                review.CompletedAt = DateTime.UtcNow;
                review.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return review;
            }

            // Step 2: Generate review with LLM
            _logger.LogInformation("Generating review with LLM ({Model})...", _options.Model);

            // Create progress tracker if we have a status comment to update
            ProgressTracker? progressTracker = null;
            if (statusCommentId.HasValue)
            {
                progressTracker = new ProgressTracker(
                    _gitHubService,
                    _loggerFactory.CreateLogger<ProgressTracker>(),
                    request.Owner,
                    request.Repository,
                    statusCommentId.Value);
            }

            var reviewResponse = await _ollamaService.GenerateReviewAsync(request, diffs, cancellationToken, progressTracker);

            // Step 3: Post main review to GitHub first
            _logger.LogInformation("Posting main review to GitHub...");

            try
            {
                const int safeCommentLength = 60000;
                var mainReviewBody = BuildReviewBody(reviewResponse);

                if (statusCommentId.HasValue && mainReviewBody.Length < safeCommentLength && reviewResponse.Comments.Count == 0)
                {
                    // Small review with no inline comments - update the ack comment
                    await _gitHubService.UpdateCommentAsync(
                        request.Owner,
                        request.Repository,
                        statusCommentId.Value,
                        mainReviewBody,
                        cancellationToken);
                }
                else if (mainReviewBody.Length > safeCommentLength || reviewResponse.Comments.Count > 50)
                {
                    // Large review - split across multiple comments
                    _logger.LogWarning(
                        "Main review is too large (Summary: {SummaryLength} chars, Comments: {CommentCount}). Splitting across multiple GitHub comments.",
                        reviewResponse.Summary?.Length ?? 0,
                        reviewResponse.Comments.Count);

                    await PostSplitReviewAsync(request, reviewResponse, cancellationToken, statusCommentId);
                }
                else
                {
                    // Normal-sized review - post as PR review
                    var githubReviewId = await _gitHubService.PostReviewAsync(
                        request.Owner,
                        request.Repository,
                        request.PullRequestNumber,
                        reviewResponse,
                        cancellationToken);

                    review.GitHubReviewId = githubReviewId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post main review to GitHub, attempting fallback comment");

                // Fallback: Post as a simple comment
                var fallbackComment = BuildFallbackComment(reviewResponse);
                await _gitHubService.PostCommentAsync(
                    request.Owner,
                    request.Repository,
                    request.PullRequestNumber,
                    fallbackComment,
                    cancellationToken);
            }

            // Step 4: Post "council convened" message
            _logger.LogInformation("Posting council convened message...");
            try
            {
                await _gitHubService.PostCommentAsync(
                    request.Owner,
                    request.Repository,
                    request.PullRequestNumber,
                    "🧙‍♂️ **The Council of Greybeards has convened to weigh in on this pull request...**\n\n_Consulting domain experts for additional perspectives._",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post council convened message");
            }

            // Step 5: Consult Council of Greybeards
            _logger.LogInformation("Consulting Council of Greybeards...");
            var councilOpinions = await _councilService.ConsultCouncilAsync(request, diffs, cancellationToken, progressTracker);

            // Step 6: Post council opinions to GitHub
            _logger.LogInformation("Posting council opinions to GitHub...");
            await PostCouncilOpinionsAsync(request, councilOpinions, cancellationToken);

            // Store all feedback in database (main review + council)
            var mergedReview = MergeCouncilOpinions(reviewResponse, councilOpinions);
            review.Summary = mergedReview.Summary;

            foreach (var comment in mergedReview.Comments)
            {
                review.Comments.Add(new ReviewComment
                {
                    FilePath = comment.Path,
                    LineNumber = comment.Line ?? 0,
                    Body = comment.Body,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Mark as completed
            review.Status = "Completed";
            review.CompletedAt = DateTime.UtcNow;
            review.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Add thumbs up reaction to indicate success
            if (request.TriggerCommentId.HasValue)
            {
                try
                {
                    await _gitHubService.AddReactionToCommentAsync(
                        request.Owner,
                        request.Repository,
                        request.TriggerCommentId.Value,
                        ReactionType.Plus1,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add thumbs up reaction to comment");
                }
            }

            _logger.LogInformation(
                "Code review completed for PR #{Number} in {Duration:F1}s ({CommentCount} comments)",
                request.PullRequestNumber,
                stopwatch.Elapsed.TotalSeconds,
                reviewResponse.Comments.Count);

            return review;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code review failed for PR #{Number}: {Message}", request.PullRequestNumber, ex.Message);

            review.Status = "Failed";
            review.ErrorMessage = ex.Message;
            review.CompletedAt = DateTime.UtcNow;
            review.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Add thumbs down reaction to indicate failure
            if (request.TriggerCommentId.HasValue)
            {
                try
                {
                    await _gitHubService.AddReactionToCommentAsync(
                        request.Owner,
                        request.Repository,
                        request.TriggerCommentId.Value,
                        ReactionType.Confused,
                        cancellationToken);
                }
                catch (Exception reactionEx)
                {
                    _logger.LogWarning(reactionEx, "Failed to add thumbs down reaction to comment");
                }
            }

            // Try to post error comment to GitHub
            try
            {
                await _gitHubService.PostCommentAsync(
                    request.Owner,
                    request.Repository,
                    request.PullRequestNumber,
                    $"⚠️ Code review failed: {ex.Message}",
                    cancellationToken);
            }
            catch
            {
                // Ignore errors posting failure comment
            }

            throw;
        }
    }

    /// <summary>
    /// Check if a review already exists for this PR.
    /// </summary>
    public async Task<bool> HasRecentReviewAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        TimeSpan? maxAge = null)
    {
        maxAge ??= TimeSpan.FromHours(1); // Default: don't re-review within 1 hour

        var repository = $"{owner}/{repo}";
        var cutoff = DateTime.UtcNow.Subtract(maxAge.Value);

        return await _dbContext.CodeReviews
            .AnyAsync(r =>
                r.Repository == repository &&
                r.PullRequestNumber == pullRequestNumber &&
                r.CreatedAt >= cutoff &&
                r.Status == "Completed");
    }

    /// <summary>
    /// Post council opinions as a separate comment on GitHub.
    /// </summary>
    private async Task PostCouncilOpinionsAsync(
        ReviewRequest request,
        List<CouncilMemberOpinion> councilOpinions,
        CancellationToken cancellationToken)
    {
        var relevantOpinions = councilOpinions.Where(o => o.IsRelevant).ToList();

        if (relevantOpinions.Count == 0)
        {
            // No relevant council opinions - post a simple message
            await _gitHubService.PostCommentAsync(
                request.Owner,
                request.Repository,
                request.PullRequestNumber,
                "🧙‍♂️ **Council Consultation Complete**\n\nThe Council of Greybeards has reviewed the changes and found no significant concerns requiring their expertise.",
                cancellationToken);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 🧙‍♂️ Council of Greybeards - Expert Opinions");
        sb.AppendLine();
        sb.AppendLine($"The council has consulted {relevantOpinions.Count} expert(s) who provided feedback:");
        sb.AppendLine();

        foreach (var opinion in relevantOpinions)
        {
            sb.AppendLine($"### {opinion.AgentName}");
            sb.AppendLine();
            sb.AppendLine(opinion.Opinion);
            sb.AppendLine();

            if (opinion.Comments.Count > 0)
            {
                sb.AppendLine("**Specific concerns:**");
                foreach (var comment in opinion.Comments)
                {
                    var severity = comment.Severity.ToUpper();
                    var severityEmoji = severity switch
                    {
                        "CRITICAL" => "🔴",
                        "HIGH" => "🟠",
                        "MEDIUM" => "🟡",
                        "LOW" => "🔵",
                        _ => "ℹ️"
                    };

                    sb.AppendLine($"- {severityEmoji} **{severity}** - `{comment.Path}:{comment.Line}` - {comment.Body}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine("*Council consultation powered by domain-expert agents*");

        await _gitHubService.PostCommentAsync(
            request.Owner,
            request.Repository,
            request.PullRequestNumber,
            sb.ToString(),
            cancellationToken);
    }

    private string BuildReviewBody(ReviewResponse reviewResponse)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("## 🤖 AI Code Review");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(reviewResponse.Summary))
        {
            sb.AppendLine(reviewResponse.Summary);
        }
        else
        {
            sb.AppendLine("No issues found. The changes look good! ✅");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Powered by Dhadgar.CodeReview with DeepSeek Coder*");

        return sb.ToString();
    }

    private string BuildFallbackComment(ReviewResponse reviewResponse)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("## 🤖 AI Code Review");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(reviewResponse.Summary))
        {
            sb.AppendLine("### Summary");
            sb.AppendLine(reviewResponse.Summary);
            sb.AppendLine();
        }

        if (reviewResponse.Comments.Count > 0)
        {
            sb.AppendLine("### Comments");
            sb.AppendLine();

            foreach (var comment in reviewResponse.Comments)
            {
                sb.AppendLine($"**`{comment.Path}:{comment.Line}`**");
                sb.AppendLine(comment.Body);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No specific issues found. The changes look good! ✅");
        }

        sb.AppendLine("---");
        sb.AppendLine("*Powered by Dhadgar.CodeReview with DeepSeek Coder*");

        return sb.ToString();
    }

    /// <summary>
    /// Post a large review split across multiple GitHub comments.
    /// </summary>
    private async Task PostSplitReviewAsync(
        ReviewRequest request,
        ReviewResponse review,
        CancellationToken cancellationToken,
        long? statusCommentId = null)
    {
        const int safeCommentLength = 60000;

        // Split summary into chunks if needed
        var summaryChunks = SplitTextIntoChunks(review.Summary, safeCommentLength);

        _logger.LogInformation(
            "Posting review in {SummaryChunks} summary chunk(s) + inline comments",
            summaryChunks.Count);

        // Post summary chunks as regular comments (not review comments)
        for (int i = 0; i < summaryChunks.Count; i++)
        {
            var chunkHeader = summaryChunks.Count > 1
                ? $"## 🤖 AI Code Review (Part {i + 1}/{summaryChunks.Count})\n\n"
                : "## 🤖 AI Code Review\n\n";

            var commentBody = chunkHeader + summaryChunks[i];

            if (i == summaryChunks.Count - 1)
            {
                commentBody += "\n\n---\n*Inline comments posted separately due to review size.*";
            }

            await _gitHubService.PostCommentAsync(
                request.Owner,
                request.Repository,
                request.PullRequestNumber,
                commentBody,
                cancellationToken);

            // Rate limiting - wait between comments
            if (i < summaryChunks.Count - 1)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        // Post inline comments in batches (GitHub allows max 30 comments per review)
        const int maxCommentsPerReview = 30;
        var commentBatches = review.Comments
            .Select((comment, index) => new { comment, index })
            .GroupBy(x => x.index / maxCommentsPerReview)
            .Select(g => g.Select(x => x.comment).ToList())
            .ToList();

        _logger.LogInformation(
            "Posting {CommentCount} inline comments in {BatchCount} batch(es)",
            review.Comments.Count,
            commentBatches.Count);

        for (int i = 0; i < commentBatches.Count; i++)
        {
            var batch = commentBatches[i];

            var batchReview = new ReviewResponse
            {
                Summary = i == 0
                    ? $"Inline feedback (Batch {i + 1}/{commentBatches.Count})"
                    : $"Additional inline feedback (Batch {i + 1}/{commentBatches.Count})",
                Comments = batch
            };

            try
            {
                await _gitHubService.PostReviewAsync(
                    request.Owner,
                    request.Repository,
                    request.PullRequestNumber,
                    batchReview,
                    cancellationToken);

                // Rate limiting between batches
                if (i < commentBatches.Count - 1)
                {
                    await Task.Delay(2000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post review batch {Batch}/{Total}", i + 1, commentBatches.Count);
            }
        }
    }

    /// <summary>
    /// Split long text into chunks that fit within character limits.
    /// Tries to split on section boundaries (##) for better readability.
    /// </summary>
    private List<string> SplitTextIntoChunks(string text, int maxLength)
    {
        var chunks = new List<string>();

        if (text.Length <= maxLength)
        {
            chunks.Add(text);
            return chunks;
        }

        // Try to split on section headers (##)
        var sections = System.Text.RegularExpressions.Regex.Split(text, @"(?=^##\s)", System.Text.RegularExpressions.RegexOptions.Multiline);

        var currentChunk = new System.Text.StringBuilder();

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section))
                continue;

            // If adding this section exceeds limit, start new chunk
            if (currentChunk.Length + section.Length > maxLength && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            // If single section is too large, split it further
            if (section.Length > maxLength)
            {
                // Split on paragraph breaks
                var paragraphs = section.Split(new[] { "\n\n" }, StringSplitOptions.None);

                foreach (var paragraph in paragraphs)
                {
                    if (currentChunk.Length + paragraph.Length + 2 > maxLength && currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }

                    currentChunk.AppendLine(paragraph);
                    currentChunk.AppendLine();
                }
            }
            else
            {
                currentChunk.Append(section);
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    /// <summary>
    /// Merge Council of Greybeards opinions into the main review.
    /// </summary>
    private ReviewResponse MergeCouncilOpinions(ReviewResponse mainReview, List<CouncilMemberOpinion> councilOpinions)
    {
        var sb = new System.Text.StringBuilder();

        // Add main summary
        if (!string.IsNullOrEmpty(mainReview.Summary))
        {
            sb.AppendLine("## General Code Review");
            sb.AppendLine();
            sb.AppendLine(mainReview.Summary);
            sb.AppendLine();
        }

        // Add Council section
        sb.AppendLine("## Council of Greybeards");
        sb.AppendLine();
        sb.AppendLine("Expert opinions from specialized domain agents:");
        sb.AppendLine();

        var relevantMembers = councilOpinions.Where(o => o.IsRelevant).ToList();
        var irrelevantMembers = councilOpinions.Where(o => !o.IsRelevant).ToList();

        if (relevantMembers.Any())
        {
            foreach (var member in relevantMembers)
            {
                sb.AppendLine($"### 🧙 {FormatAgentName(member.AgentName)}");
                sb.AppendLine();
                sb.AppendLine(member.Opinion);
                sb.AppendLine();

                if (member.Comments.Any())
                {
                    sb.AppendLine($"**{member.Comments.Count} specific concern(s) raised** (see inline comments below)");
                    sb.AppendLine();
                }
            }
        }

        if (irrelevantMembers.Any())
        {
            sb.AppendLine("### Consulted (No Comments)");
            sb.AppendLine();
            foreach (var member in irrelevantMembers)
            {
                sb.AppendLine($"- **{FormatAgentName(member.AgentName)}**: {member.Opinion}");
            }
            sb.AppendLine();
        }

        // Merge all comments (main + council)
        var allComments = new List<ReviewCommentDto>(mainReview.Comments);

        foreach (var member in relevantMembers)
        {
            foreach (var comment in member.Comments)
            {
                var severityEmoji = comment.Severity.ToLower() switch
                {
                    "critical" => "🚨",
                    "high" => "⚠️",
                    "medium" => "⚡",
                    "low" => "💡",
                    _ => "ℹ️"
                };

                allComments.Add(new ReviewCommentDto
                {
                    Path = comment.Path,
                    Line = comment.Line,
                    Body = $"{severityEmoji} **[{FormatAgentName(member.AgentName)}]** {comment.Body}"
                });
            }
        }

        return new ReviewResponse
        {
            Summary = sb.ToString(),
            Comments = allComments
        };
    }

    /// <summary>
    /// Format agent name for display (convert kebab-case to Title Case).
    /// </summary>
    private string FormatAgentName(string agentName)
    {
        return string.Join(" ", agentName.Split('-').Select(word =>
            char.ToUpper(word[0]) + word.Substring(1)));
    }
}

/// <summary>
/// Configuration options for code review behavior.
/// </summary>
public class ReviewOptions
{
    public int MaxDiffSize { get; set; } = 50000;
    public int MaxFilesPerReview { get; set; } = 20;
    public bool EnableAutoReview { get; set; } = false;
    public string? Model { get; set; }
}
