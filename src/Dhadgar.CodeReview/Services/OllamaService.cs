using Dhadgar.CodeReview.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Service for interacting with Ollama LLM for code review generation.
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    /// <summary>
    /// Generate code review comments using the LLM.
    /// </summary>
    public async Task<ReviewResponse> GenerateReviewAsync(
        ReviewRequest request,
        List<FileDiff> diffs,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating review for PR #{Number} in {Repo} using {Model}",
            request.PullRequestNumber,
            request.Repository,
            _options.Model);

        const int maxContextTokens = 16384;
        const int reservedTokensForResponse = 4096;
        const int maxPromptTokens = maxContextTokens - reservedTokensForResponse;

        var prompt = BuildReviewPrompt(request, diffs);
        var estimatedTokens = EstimateTokenCount(prompt);

        _logger.LogInformation(
            "Estimated prompt tokens: {Tokens} (max: {Max})",
            estimatedTokens,
            maxPromptTokens);

        // If prompt fits in context window, process normally
        if (estimatedTokens <= maxPromptTokens)
        {
            var response = await CallOllamaAsync(prompt, cancellationToken);
            return ParseReviewResponse(response);
        }

        // Otherwise, chunk the files and merge results
        _logger.LogWarning(
            "PR exceeds context window ({Tokens} tokens). Chunking into multiple reviews.",
            estimatedTokens);

        return await GenerateChunkedReviewAsync(request, diffs, maxPromptTokens, cancellationToken);
    }

    private string BuildReviewPrompt(ReviewRequest request, List<FileDiff> diffs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert code reviewer specializing in C# and .NET. Analyze the following pull request changes and provide constructive feedback.");
        sb.AppendLine();
        sb.AppendLine("Focus on:");
        sb.AppendLine("- Potential bugs and edge cases");
        sb.AppendLine("- Security vulnerabilities (SQL injection, XSS, authentication issues, etc.)");
        sb.AppendLine("- Performance issues and optimization opportunities");
        sb.AppendLine("- Code style and .NET best practices");
        sb.AppendLine("- Maintainability and readability concerns");
        sb.AppendLine("- Null safety and exception handling");
        sb.AppendLine();
        sb.AppendLine($"PR Title: {request.PullRequestTitle}");
        sb.AppendLine($"PR Description: {request.PullRequestBody ?? "(no description)"}");
        sb.AppendLine();
        sb.AppendLine("Changed Files:");
        sb.AppendLine();

        foreach (var diff in diffs)
        {
            sb.AppendLine($"File: {diff.FileName} ({diff.Status})");
            sb.AppendLine($"Changes: +{diff.Additions} -{diff.Deletions}");

            if (!string.IsNullOrEmpty(diff.Patch))
            {
                sb.AppendLine("```diff");
                sb.AppendLine(diff.Patch);
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Please provide your review in the following JSON format:");
        sb.AppendLine("{");
        sb.AppendLine("  \"comments\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"path\": \"src/Example.cs\",");
        sb.AppendLine("      \"line\": 42,");
        sb.AppendLine("      \"body\": \"Consider adding null-checking here to prevent NullReferenceException.\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"summary\": \"Overall assessment of the changes with key highlights.\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Only include comments for significant issues. Avoid nitpicks. Be constructive and specific.");

        return sb.ToString();
    }

    /// <summary>
    /// Call Ollama with a custom system prompt (for Council agents).
    /// </summary>
    public async Task<string> CallOllamaWithSystemPromptAsync(
        string prompt,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        var fullPrompt = $"{systemPrompt}\n\n---\n\n{prompt}";
        return await CallOllamaAsync(fullPrompt, cancellationToken);
    }

    private async Task<string> CallOllamaAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _options.Model,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = 0.3, // Lower temperature for more focused, deterministic reviews
                num_predict = 4096, // Max tokens to generate (increased for larger reviews)
                num_ctx = 16384, // Maximum context window for DeepSeek Coder 33B
                num_gpu = 999, // Offload all layers to GPU
                num_thread = 8 // CPU threads for non-GPU operations
            }
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending request to Ollama at {BaseUrl}/api/generate", _options.BaseUrl);

        var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl}/api/generate",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

        if (ollamaResponse?.Response == null)
        {
            throw new InvalidOperationException("Invalid response from Ollama");
        }

        _logger.LogInformation("Received response from Ollama ({Length} characters)", ollamaResponse.Response.Length);

        return ollamaResponse.Response;
    }

    private ReviewResponse ParseReviewResponse(string llmResponse)
    {
        try
        {
            // Try to extract JSON from the response (LLM might include extra text)
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var reviewResponse = JsonSerializer.Deserialize<ReviewResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (reviewResponse != null)
                {
                    _logger.LogInformation("Parsed review response: {CommentCount} comments", reviewResponse.Comments.Count);
                    return reviewResponse;
                }
            }

            _logger.LogWarning("Could not parse JSON from LLM response, returning empty review");
            return new ReviewResponse
            {
                Summary = "Failed to parse review response from LLM. Raw response:\n" + llmResponse
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing review response as JSON");
            return new ReviewResponse
            {
                Summary = $"Error parsing review: {ex.Message}\n\nRaw response:\n{llmResponse}"
            };
        }
    }

    /// <summary>
    /// Generate review by chunking files into multiple LLM calls and merging results.
    /// </summary>
    private async Task<ReviewResponse> GenerateChunkedReviewAsync(
        ReviewRequest request,
        List<FileDiff> diffs,
        int maxPromptTokens,
        CancellationToken cancellationToken)
    {
        var chunks = CreateChunks(request, diffs, maxPromptTokens);

        _logger.LogInformation(
            "Split PR into {ChunkCount} chunks for processing",
            chunks.Count);

        var allComments = new List<ReviewCommentDto>();
        var summaries = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("Processing chunk {Current}/{Total}", i + 1, chunks.Count);

            var chunkPrompt = BuildChunkPrompt(request, chunks[i], i + 1, chunks.Count);
            var response = await CallOllamaAsync(chunkPrompt, cancellationToken);
            var chunkResult = ParseReviewResponse(response);

            allComments.AddRange(chunkResult.Comments);
            if (!string.IsNullOrEmpty(chunkResult.Summary))
            {
                summaries.Add($"**Part {i + 1}/{chunks.Count}:** {chunkResult.Summary}");
            }
        }

        // Merge all chunk results into single review
        var mergedSummary = string.Join("\n\n", summaries);
        if (summaries.Count > 1)
        {
            mergedSummary = $"This large PR was reviewed in {chunks.Count} parts:\n\n{mergedSummary}";
        }

        _logger.LogInformation(
            "Merged {ChunkCount} chunks into single review with {CommentCount} total comments",
            chunks.Count,
            allComments.Count);

        return new ReviewResponse
        {
            Summary = mergedSummary,
            Comments = allComments
        };
    }

    /// <summary>
    /// Create chunks of files that fit within token limits.
    /// </summary>
    private List<List<FileDiff>> CreateChunks(ReviewRequest request, List<FileDiff> diffs, int maxPromptTokens)
    {
        var chunks = new List<List<FileDiff>>();
        var currentChunk = new List<FileDiff>();

        // Calculate base overhead (PR metadata)
        var basePrompt = BuildReviewPrompt(request, new List<FileDiff>());
        var baseTokens = EstimateTokenCount(basePrompt);

        var currentTokens = baseTokens;

        foreach (var diff in diffs)
        {
            // Estimate tokens for this file
            var fileText = $"File: {diff.FileName}\n{diff.Patch ?? ""}";
            var fileTokens = EstimateTokenCount(fileText);

            // If adding this file exceeds limit, start new chunk
            if (currentTokens + fileTokens > maxPromptTokens && currentChunk.Count > 0)
            {
                chunks.Add(currentChunk);
                currentChunk = new List<FileDiff>();
                currentTokens = baseTokens;
            }

            currentChunk.Add(diff);
            currentTokens += fileTokens;
        }

        // Add remaining files
        if (currentChunk.Count > 0)
        {
            chunks.Add(currentChunk);
        }

        return chunks;
    }

    /// <summary>
    /// Build prompt for a chunk of files.
    /// </summary>
    private string BuildChunkPrompt(ReviewRequest request, List<FileDiff> chunk, int chunkNumber, int totalChunks)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert code reviewer specializing in C# and .NET. Analyze the following pull request changes and provide constructive feedback.");
        sb.AppendLine();

        if (totalChunks > 1)
        {
            sb.AppendLine($"**Note:** This is part {chunkNumber} of {totalChunks} of a large PR. Focus on the files in this chunk.");
            sb.AppendLine();
        }

        sb.AppendLine("Focus on:");
        sb.AppendLine("- Potential bugs and edge cases");
        sb.AppendLine("- Security vulnerabilities (SQL injection, XSS, authentication issues, etc.)");
        sb.AppendLine("- Performance issues and optimization opportunities");
        sb.AppendLine("- Code style and .NET best practices");
        sb.AppendLine("- Maintainability and readability concerns");
        sb.AppendLine("- Null safety and exception handling");
        sb.AppendLine();
        sb.AppendLine($"PR Title: {request.PullRequestTitle}");
        sb.AppendLine($"PR Description: {request.PullRequestBody ?? "(no description)"}");
        sb.AppendLine();
        sb.AppendLine($"Changed Files (Part {chunkNumber}/{totalChunks}):");
        sb.AppendLine();

        foreach (var diff in chunk)
        {
            sb.AppendLine($"File: {diff.FileName} ({diff.Status})");
            sb.AppendLine($"Changes: +{diff.Additions} -{diff.Deletions}");

            if (!string.IsNullOrEmpty(diff.Patch))
            {
                sb.AppendLine("```diff");
                sb.AppendLine(diff.Patch);
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Please provide your review in the following JSON format:");
        sb.AppendLine("{");
        sb.AppendLine("  \"comments\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"path\": \"src/Example.cs\",");
        sb.AppendLine("      \"line\": 42,");
        sb.AppendLine("      \"body\": \"Consider adding null-checking here to prevent NullReferenceException.\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"summary\": \"Summary of issues found in this part of the PR.\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Only include comments for significant issues. Avoid nitpicks. Be constructive and specific.");

        return sb.ToString();
    }

    /// <summary>
    /// Estimate token count for text (rough approximation: 4 chars per token).
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        // Rough approximation: 1 token ≈ 4 characters for English text
        // This is conservative to avoid exceeding limits
        return text.Length / 4;
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}

/// <summary>
/// Configuration options for Ollama service.
/// </summary>
public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "deepseek-coder:33b";
    public int TimeoutSeconds { get; set; } = 300;
}
