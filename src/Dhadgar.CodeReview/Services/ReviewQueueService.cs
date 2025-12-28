using Dhadgar.CodeReview.Models;
using System.Threading.Channels;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Manages a queue of review requests to ensure sequential processing.
/// </summary>
public class ReviewQueueService : BackgroundService
{
    private readonly Channel<QueuedReviewRequest> _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReviewQueueService> _logger;

    public ReviewQueueService(
        IServiceProvider serviceProvider,
        ILogger<ReviewQueueService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _queue = Channel.CreateUnbounded<QueuedReviewRequest>();
    }

    /// <summary>
    /// Enqueue a review request for processing.
    /// </summary>
    public async Task EnqueueReviewAsync(ReviewRequest request, long? statusCommentId = null)
    {
        var queuedRequest = new QueuedReviewRequest
        {
            Request = request,
            StatusCommentId = statusCommentId,
            EnqueuedAt = DateTime.UtcNow
        };

        await _queue.Writer.WriteAsync(queuedRequest);

        _logger.LogInformation(
            "Enqueued review for PR #{Number} in {Repo}",
            request.PullRequestNumber,
            $"{request.Owner}/{request.Repository}");
    }

    /// <summary>
    /// Get the current queue position for logging/status updates.
    /// </summary>
    public int GetQueueLength()
    {
        return _queue.Reader.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Review queue service started");

        await foreach (var queuedRequest in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var queuePosition = GetQueueLength() + 1; // Current item + remaining

                _logger.LogInformation(
                    "Processing review for PR #{Number} (queue position: {Position})",
                    queuedRequest.Request.PullRequestNumber,
                    queuePosition);

                // Create a new scope for each review
                using var scope = _serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ReviewOrchestrator>();
                var githubService = scope.ServiceProvider.GetRequiredService<GitHubService>();

                // Update status comment if provided
                if (queuedRequest.StatusCommentId.HasValue)
                {
                    try
                    {
                        await githubService.UpdateCommentAsync(
                            queuedRequest.Request.Owner,
                            queuedRequest.Request.Repository,
                            queuedRequest.StatusCommentId.Value,
                            $"🚀 **Review in progress...**\n\nStarted processing at {DateTime.UtcNow:HH:mm:ss} UTC",
                            stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update status comment");
                    }
                }

                // Perform the review
                await orchestrator.PerformReviewAsync(queuedRequest.Request, stoppingToken, queuedRequest.StatusCommentId);

                _logger.LogInformation(
                    "Completed review for PR #{Number}",
                    queuedRequest.Request.PullRequestNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process review for PR #{Number}: {Message}",
                    queuedRequest.Request.PullRequestNumber,
                    ex.Message);
            }
        }

        _logger.LogInformation("Review queue service stopped");
    }
}

/// <summary>
/// Internal wrapper for queued review requests.
/// </summary>
internal class QueuedReviewRequest
{
    public required ReviewRequest Request { get; set; }
    public long? StatusCommentId { get; set; }
    public DateTime EnqueuedAt { get; set; }
}
