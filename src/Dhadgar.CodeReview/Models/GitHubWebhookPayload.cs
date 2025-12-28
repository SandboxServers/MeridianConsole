using System.Text.Json.Serialization;

namespace Dhadgar.CodeReview.Models;

/// <summary>
/// Simplified GitHub webhook payload for pull request events.
/// Only includes fields we need for code review.
/// </summary>
public class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequest? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [JsonPropertyName("comment")]
    public Comment? Comment { get; set; }

    [JsonPropertyName("issue")]
    public Issue? Issue { get; set; }
}

public class PullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("head")]
    public GitRef? Head { get; set; }

    [JsonPropertyName("base")]
    public GitRef? Base { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

public class Repository
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("owner")]
    public Owner? Owner { get; set; }
}

public class Owner
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

public class GitRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("sha")]
    public string? Sha { get; set; }
}

public class Comment
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("user")]
    public User? User { get; set; }
}

public class User
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class Issue
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("pull_request")]
    public object? PullRequestRef { get; set; } // If not null, this issue is a PR
}
