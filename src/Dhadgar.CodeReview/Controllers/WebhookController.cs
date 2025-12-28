using Dhadgar.CodeReview.Models;
using Dhadgar.CodeReview.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Octokit;
using System.Text;
using System.Text.Json;

namespace Dhadgar.CodeReview.Controllers;

/// <summary>
/// Controller for receiving GitHub webhook events.
/// </summary>
[ApiController]
[Route("[controller]")]
public class WebhookController : ControllerBase
{
    private readonly GitHubService _gitHubService;
    private readonly ReviewQueueService _queueService;
    private readonly ReviewOptions _reviewOptions;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        GitHubService gitHubService,
        ReviewQueueService queueService,
        IOptions<ReviewOptions> reviewOptions,
        ILogger<WebhookController> logger)
    {
        _gitHubService = gitHubService;
        _queueService = queueService;
        _reviewOptions = reviewOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Receive GitHub webhook events.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken cancellationToken)
    {
        // Read raw body for signature verification
        Request.EnableBuffering();
        var body = await new StreamReader(Request.Body, Encoding.UTF8).ReadToEndAsync();
        Request.Body.Position = 0;

        // Verify signature
        var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!_gitHubService.VerifyWebhookSignature(body, signatureHeader))
        {
            _logger.LogWarning("Invalid webhook signature");
            return Unauthorized("Invalid signature");
        }

        // Get event type
        var eventType = Request.Headers["X-GitHub-Event"].ToString();

        try
        {
            var payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload == null)
            {
                return BadRequest("Invalid payload");
            }

            // Handle pull request events
            if (eventType == "pull_request")
            {
                return await HandlePullRequestEvent(payload, cancellationToken);
            }

            // Handle issue comment events (for /dhadgar command)
            if (eventType == "issue_comment")
            {
                return await HandleIssueCommentEvent(payload, cancellationToken);
            }

            // Silent ignore for other event types
            return Ok("Event ignored");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook: {Message}", ex.Message);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<IActionResult> HandlePullRequestEvent(
        GitHubWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.PullRequest == null || payload.Repository == null)
        {
            return BadRequest("Missing pull request or repository data");
        }

        var action = payload.Action;

        _logger.LogInformation(
            "Pull request #{Number} {Action} in {Repo}",
            payload.PullRequest.Number,
            action,
            payload.Repository.FullName);

        // Only trigger on synchronize (new commits) if auto-review is enabled
        if (action == "synchronize" && _reviewOptions.EnableAutoReview)
        {
            await TriggerReviewAsync(payload, "webhook", cancellationToken);
            return Accepted("Review triggered");
        }

        // Ignore other PR actions
        return Ok($"Ignored action: {action}");
    }

    private async Task<IActionResult> HandleIssueCommentEvent(
        GitHubWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        // Silent checks - only log when /dhadgar is found
        if (payload.Action != "created")
        {
            return Ok("Not a created comment");
        }

        if (payload.Comment == null || payload.Repository == null || payload.Issue == null)
        {
            return BadRequest("Missing comment, repository, or issue data");
        }

        // Ignore comments from bots
        if (payload.Comment.User?.Type == "Bot")
        {
            return Ok("Ignored bot comment");
        }

        // Check if this is a pull request (issues and PRs share the same comment endpoint)
        if (payload.Issue.PullRequestRef == null)
        {
            return Ok("Not a pull request comment");
        }

        // Check for /dhadgar command
        var commentBody = payload.Comment.Body?.Trim() ?? "";

        if (!commentBody.Equals("/dhadgar", StringComparison.OrdinalIgnoreCase))
        {
            return Ok("Not a dhadgar command");
        }

        _logger.LogInformation(
            "Dhadgar review command triggered by {User} on PR #{Number} in {Repo}",
            payload.Comment.User?.Login,
            payload.Issue.Number,
            payload.Repository.FullName);

        // Add rocket reaction to acknowledge the command
        var parts = payload.Repository.FullName?.Split('/');
        if (parts != null && parts.Length == 2)
        {
            try
            {
                await _gitHubService.AddReactionToCommentAsync(
                    parts[0],
                    parts[1],
                    payload.Comment.Id,
                    ReactionType.Rocket,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add rocket reaction to comment");
            }
        }

        // Create PR payload structure for triggering review
        var prPayload = new GitHubWebhookPayload
        {
            PullRequest = new Models.PullRequest
            {
                Number = payload.Issue.Number
            },
            Repository = payload.Repository,
            Comment = payload.Comment // Pass comment for reaction tracking
        };

        await TriggerReviewAsync(prPayload, "comment", cancellationToken);

        return Accepted("Review triggered by command");
    }

    private async Task TriggerReviewAsync(
        GitHubWebhookPayload payload,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        if (payload.PullRequest == null || payload.Repository == null)
        {
            _logger.LogWarning("Cannot trigger review: missing PR or repository data");
            return;
        }

        var parts = payload.Repository.FullName?.Split('/');
        if (parts == null || parts.Length != 2)
        {
            _logger.LogWarning("Invalid repository name: {FullName}", payload.Repository.FullName);
            return;
        }

        var owner = parts[0];
        var repo = parts[1];

        var request = new ReviewRequest
        {
            Owner = owner,
            Repository = repo,
            PullRequestNumber = payload.PullRequest.Number,
            PullRequestTitle = payload.PullRequest.Title,
            PullRequestBody = payload.PullRequest.Body,
            TriggerSource = triggerSource,
            TriggerCommentId = payload.Comment?.Id
        };

        // Post an acknowledgment comment
        long? statusCommentId = null;
        try
        {
            var queuePosition = _queueService.GetQueueLength() + 1;
            var ackMessage = queuePosition > 1
                ? $"🚀 **Review queued** (position {queuePosition})\n\nYour review will start shortly."
                : "🚀 **Review starting...**\n\nAnalyzing changes with the Council of Greybeards.";

            statusCommentId = await _gitHubService.PostCommentAsync(
                owner,
                repo,
                payload.PullRequest.Number,
                ackMessage,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post acknowledgment comment");
        }

        // Enqueue the review request
        await _queueService.EnqueueReviewAsync(request, statusCommentId);
    }
}
