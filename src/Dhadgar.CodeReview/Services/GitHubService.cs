using Dhadgar.CodeReview.Models;
using GitHubJwt;
using Microsoft.Extensions.Options;
using Octokit;
using System.Security.Cryptography;
using System.Text;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Service for interacting with GitHub API using Octokit.
/// </summary>
public class GitHubService
{
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubService> _logger;
    private string? _privateKey;

    public GitHubService(
        IOptions<GitHubOptions> options,
        ILogger<GitHubService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Verify GitHub webhook signature.
    /// </summary>
    public bool VerifyWebhookSignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("Webhook secret not configured, skipping signature verification");
            return true; // Allow in dev if not configured
        }

        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256="))
        {
            return false;
        }

        var signature = signatureHeader.Substring(7); // Remove "sha256=" prefix

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        return signature.Equals(expectedSignature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get authenticated GitHub client for installation.
    /// </summary>
    private async Task<GitHubClient> GetGitHubClientAsync()
    {
        // Load private key
        if (_privateKey == null)
        {
            if (!File.Exists(_options.PrivateKeyPath))
            {
                throw new InvalidOperationException($"GitHub App private key not found at: {_options.PrivateKeyPath}");
            }

            _privateKey = await File.ReadAllTextAsync(_options.PrivateKeyPath);
            _privateKey = _privateKey.Trim(); // Remove any leading/trailing whitespace

            _logger.LogInformation(
                "Loaded GitHub App private key ({Length} chars, starts with: {Start}, ends with: {End})",
                _privateKey.Length,
                _privateKey[..Math.Min(50, _privateKey.Length)],
                _privateKey[Math.Max(0, _privateKey.Length - 50)..]);
        }

        // Generate JWT token for GitHub App
        _logger.LogInformation("Creating JWT token with AppId: {AppId}", _options.AppId);
        var generator = new GitHubJwtFactory(
            new FilePrivateKeySource(_options.PrivateKeyPath),  // Use file path directly instead of string
            new GitHubJwtFactoryOptions
            {
                AppIntegrationId = int.Parse(_options.AppId),
                ExpirationSeconds = 600 // 10 minutes (max allowed)
            });

        _logger.LogInformation("Generating JWT token...");
        var jwtToken = generator.CreateEncodedJwtToken();
        _logger.LogInformation("JWT token generated successfully");

        // Create client with JWT
        var appClient = new GitHubClient(new ProductHeaderValue("DhadgarCodeReview"))
        {
            Credentials = new Credentials(jwtToken, AuthenticationType.Bearer)
        };

        // Get installation access token
        var response = await appClient.GitHubApps.CreateInstallationToken(long.Parse(_options.InstallationId));

        // Create authenticated client with installation token
        var client = new GitHubClient(new ProductHeaderValue("DhadgarCodeReview"))
        {
            Credentials = new Credentials(response.Token)
        };

        _logger.LogDebug("Created authenticated GitHub client for installation {InstallationId}", _options.InstallationId);

        return client;
    }

    /// <summary>
    /// Get pull request diff (changed files with patches).
    /// </summary>
    public async Task<List<FileDiff>> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        var client = await GetGitHubClientAsync();

        _logger.LogInformation("Fetching diff for PR #{Number} in {Owner}/{Repo}", pullRequestNumber, owner, repo);

        var files = await client.PullRequest.Files(owner, repo, pullRequestNumber);

        var diffs = new List<FileDiff>();

        foreach (var file in files)
        {
            diffs.Add(new FileDiff
            {
                FileName = file.FileName,
                Patch = file.Patch,
                Additions = file.Additions,
                Deletions = file.Deletions,
                Status = file.Status
            });
        }

        _logger.LogInformation("Retrieved {Count} changed files for PR #{Number}", diffs.Count, pullRequestNumber);

        return diffs;
    }

    /// <summary>
    /// Post review comments to a pull request.
    /// </summary>
    public async Task<long> PostReviewAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        ReviewResponse reviewResponse,
        CancellationToken cancellationToken = default)
    {
        var client = await GetGitHubClientAsync();

        _logger.LogInformation(
            "Posting review with {CommentCount} comments to PR #{Number} in {Owner}/{Repo}",
            reviewResponse.Comments.Count,
            pullRequestNumber,
            owner,
            repo);

        // Create pull request review with comments
        var review = new PullRequestReviewCreate
        {
            Event = PullRequestReviewEvent.Comment,
            Body = BuildReviewBody(reviewResponse)
        };

        // Add inline comments
        foreach (var comment in reviewResponse.Comments)
        {
            review.Comments.Add(new DraftPullRequestReviewComment(
                path: comment.Path,
                body: comment.Body,
                position: comment.Line
            ));
        }

        try
        {
            var createdReview = await client.PullRequest.Review.Create(owner, repo, pullRequestNumber, review);

            _logger.LogInformation("Successfully posted review {ReviewId} to PR #{Number}", createdReview.Id, pullRequestNumber);

            return createdReview.Id;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to post review to GitHub: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Post a simple comment to a pull request (fallback if review fails).
    /// </summary>
    public async Task PostCommentAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string comment,
        CancellationToken cancellationToken = default)
    {
        var client = await GetGitHubClientAsync();

        _logger.LogInformation("Posting comment to PR #{Number} in {Owner}/{Repo}", pullRequestNumber, owner, repo);

        await client.Issue.Comment.Create(owner, repo, pullRequestNumber, comment);
    }

    private string BuildReviewBody(ReviewResponse reviewResponse)
    {
        var sb = new StringBuilder();

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
            sb.AppendLine($"I've left {reviewResponse.Comments.Count} inline comment(s) on specific lines.");
        }
        else
        {
            sb.AppendLine("No specific issues found. The changes look good! ✅");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Powered by Dhadgar.CodeReview with DeepSeek Coder*");

        return sb.ToString();
    }
}

/// <summary>
/// Configuration options for GitHub integration.
/// </summary>
public class GitHubOptions
{
    public string AppId { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "./secrets/github-app.pem";
    public string WebhookSecret { get; set; } = "";
}
