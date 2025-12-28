using Dhadgar.CodeReview.Models;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Service for orchestrating the "Council of Greybeards" - consulting all available agents
/// in the .claude/agents directory for their expert opinions on code changes.
/// </summary>
public class CouncilService
{
    private readonly OllamaService _ollamaService;
    private readonly ILogger<CouncilService> _logger;
    private readonly string _agentsDirectory;

    public CouncilService(
        OllamaService ollamaService,
        ILogger<CouncilService> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _ollamaService = ollamaService;
        _logger = logger;

        // Try to get agents directory from configuration, fallback to development location
        _agentsDirectory = configuration.GetValue<string>("AgentsDirectory")
            ?? Path.Combine(environment.ContentRootPath, "agents");

        // Fallback for development: navigate up to repo root
        if (!Directory.Exists(_agentsDirectory))
        {
            var repoRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
            _agentsDirectory = Path.Combine(repoRoot, ".claude", "agents");
        }
    }

    /// <summary>
    /// Consult the Council of Greybeards for their expert opinions on the PR.
    /// </summary>
    public async Task<List<CouncilMemberOpinion>> ConsultCouncilAsync(
        ReviewRequest request,
        List<FileDiff> diffs,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Consulting Council of Greybeards for PR #{Number}", request.PullRequestNumber);

        if (!Directory.Exists(_agentsDirectory))
        {
            _logger.LogWarning("Agents directory not found: {Path}", _agentsDirectory);
            return new List<CouncilMemberOpinion>();
        }

        var agentFiles = Directory.GetFiles(_agentsDirectory, "*.md");
        _logger.LogInformation("Found {Count} council members to consult", agentFiles.Length);

        var opinions = new List<CouncilMemberOpinion>();

        foreach (var agentFile in agentFiles)
        {
            try
            {
                var opinion = await ConsultAgentAsync(agentFile, request, diffs, cancellationToken);
                opinions.Add(opinion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to consult agent {Agent}", Path.GetFileNameWithoutExtension(agentFile));

                opinions.Add(new CouncilMemberOpinion
                {
                    AgentName = Path.GetFileNameWithoutExtension(agentFile),
                    IsRelevant = false,
                    Opinion = $"Error consulting agent: {ex.Message}"
                });
            }
        }

        return opinions;
    }

    /// <summary>
    /// Consult a single agent from the council.
    /// </summary>
    private async Task<CouncilMemberOpinion> ConsultAgentAsync(
        string agentFile,
        ReviewRequest request,
        List<FileDiff> diffs,
        CancellationToken cancellationToken)
    {
        var agentName = Path.GetFileNameWithoutExtension(agentFile);
        var agentContent = await File.ReadAllTextAsync(agentFile, cancellationToken);

        // Parse agent metadata and prompt
        var (name, description, systemPrompt) = ParseAgentFile(agentContent);

        _logger.LogInformation("Consulting {Agent}...", name);

        // Build specialized prompt for this agent
        var prompt = BuildAgentReviewPrompt(name, description, systemPrompt, request, diffs);

        // Check if prompt exceeds context window
        const int maxContextTokens = 16384;
        const int reservedTokensForResponse = 4096;
        const int maxPromptTokens = maxContextTokens - reservedTokensForResponse;

        var estimatedTokens = EstimateTokenCount(prompt + systemPrompt);

        CouncilMemberOpinion opinion;

        if (estimatedTokens <= maxPromptTokens)
        {
            // Single call - prompt fits in context window
            var response = await _ollamaService.CallOllamaWithSystemPromptAsync(prompt, systemPrompt, cancellationToken);
            opinion = ParseAgentResponse(name, response);
        }
        else
        {
            // Chunked consultation - prompt too large
            _logger.LogWarning(
                "{Agent}: Prompt exceeds context window ({Tokens} tokens). Chunking consultation.",
                name,
                estimatedTokens);

            opinion = await ConsultAgentChunkedAsync(name, description, systemPrompt, request, diffs, maxPromptTokens, cancellationToken);
        }

        _logger.LogInformation(
            "{Agent}: {Relevance} - {CommentCount} comments",
            name,
            opinion.IsRelevant ? "Relevant" : "Not relevant",
            opinion.Comments.Count);

        return opinion;
    }

    /// <summary>
    /// Consult an agent by chunking the diff into multiple LLM calls.
    /// </summary>
    private async Task<CouncilMemberOpinion> ConsultAgentChunkedAsync(
        string agentName,
        string description,
        string systemPrompt,
        ReviewRequest request,
        List<FileDiff> diffs,
        int maxPromptTokens,
        CancellationToken cancellationToken)
    {
        var chunks = CreateChunks(request, diffs, maxPromptTokens, agentName, description, systemPrompt);

        _logger.LogInformation(
            "{Agent}: Split PR into {ChunkCount} chunks for processing",
            agentName,
            chunks.Count);

        var allComments = new List<CouncilComment>();
        var summaries = new List<string>();
        var isRelevant = false;

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("{Agent}: Processing chunk {Current}/{Total}", agentName, i + 1, chunks.Count);

            var chunkPrompt = BuildAgentChunkPrompt(agentName, description, systemPrompt, request, chunks[i], i + 1, chunks.Count);
            var response = await _ollamaService.CallOllamaWithSystemPromptAsync(chunkPrompt, systemPrompt, cancellationToken);
            var chunkOpinion = ParseAgentResponse(agentName, response);

            // If any chunk is relevant, the overall opinion is relevant
            if (chunkOpinion.IsRelevant)
            {
                isRelevant = true;
                allComments.AddRange(chunkOpinion.Comments);

                if (!string.IsNullOrEmpty(chunkOpinion.Opinion))
                {
                    summaries.Add($"**Part {i + 1}/{chunks.Count}:** {chunkOpinion.Opinion}");
                }
            }
        }

        // Merge chunk results
        var mergedOpinion = isRelevant && summaries.Count > 0
            ? $"This large PR was reviewed in {chunks.Count} parts:\n\n{string.Join("\n\n", summaries)}"
            : "No significant concerns found in my area of expertise.";

        _logger.LogInformation(
            "{Agent}: Merged {ChunkCount} chunks - {CommentCount} total comments",
            agentName,
            chunks.Count,
            allComments.Count);

        return new CouncilMemberOpinion
        {
            AgentName = agentName,
            IsRelevant = isRelevant,
            Opinion = mergedOpinion,
            Comments = allComments
        };
    }

    /// <summary>
    /// Create chunks of files that fit within token limits for an agent.
    /// </summary>
    private List<List<FileDiff>> CreateChunks(
        ReviewRequest request,
        List<FileDiff> diffs,
        int maxPromptTokens,
        string agentName,
        string description,
        string systemPrompt)
    {
        var chunks = new List<List<FileDiff>>();
        var currentChunk = new List<FileDiff>();

        // Calculate base overhead (PR metadata + agent context)
        var basePrompt = BuildAgentReviewPrompt(agentName, description, systemPrompt, request, new List<FileDiff>());
        var baseTokens = EstimateTokenCount(basePrompt + systemPrompt);

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
    /// Build prompt for a chunk of files for an agent.
    /// </summary>
    private string BuildAgentChunkPrompt(
        string agentName,
        string description,
        string systemPrompt,
        ReviewRequest request,
        List<FileDiff> chunk,
        int chunkNumber,
        int totalChunks)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are consulting as part of the 'Council of Greybeards' - a panel of expert agents reviewing a pull request.");
        sb.AppendLine();
        sb.AppendLine($"**Your role**: {agentName}");
        sb.AppendLine($"**Your expertise**: {description}");
        sb.AppendLine();

        if (totalChunks > 1)
        {
            sb.AppendLine($"**Note**: This is part {chunkNumber} of {totalChunks} of a large PR. Focus on the files in this chunk from your area of expertise.");
            sb.AppendLine();
        }

        sb.AppendLine("**Task**: Review the following PR changes from your area of expertise.");
        sb.AppendLine();
        sb.AppendLine("**IMPORTANT**: If this chunk is not relevant to your domain of expertise, respond with:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"relevant\": false,");
        sb.AppendLine("  \"reason\": \"Brief explanation why this chunk doesn't require my expertise\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If the chunk **is** relevant to your expertise, provide a detailed review:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"relevant\": true,");
        sb.AppendLine("  \"summary\": \"Your assessment of this chunk from your expert perspective\",");
        sb.AppendLine("  \"comments\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"path\": \"src/Example.cs\",");
        sb.AppendLine("      \"line\": 42,");
        sb.AppendLine("      \"severity\": \"critical|high|medium|low|info\",");
        sb.AppendLine("      \"body\": \"Your expert opinion on this specific issue\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## PR Details");
        sb.AppendLine();
        sb.AppendLine($"**Title**: {request.PullRequestTitle}");
        sb.AppendLine($"**Description**: {request.PullRequestBody ?? "(no description)"}");
        sb.AppendLine($"**Repository**: {request.Owner}/{request.Repository}");
        sb.AppendLine();
        sb.AppendLine($"## Changed Files (Part {chunkNumber}/{totalChunks})");
        sb.AppendLine();

        foreach (var diff in chunk)
        {
            sb.AppendLine($"### {diff.FileName} ({diff.Status})");
            sb.AppendLine($"Changes: +{diff.Additions} -{diff.Deletions}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(diff.Patch))
            {
                sb.AppendLine("```diff");
                sb.AppendLine(diff.Patch);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

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

    /// <summary>
    /// Parse agent markdown file to extract name, description, and system prompt.
    /// </summary>
    private (string name, string description, string systemPrompt) ParseAgentFile(string content)
    {
        // Extract frontmatter (YAML between --- markers)
        var frontmatterMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---", RegexOptions.Singleline);

        var name = "unknown";
        var description = "";

        if (frontmatterMatch.Success)
        {
            var frontmatter = frontmatterMatch.Groups[1].Value;

            var nameMatch = Regex.Match(frontmatter, @"name:\s*(.+)");
            if (nameMatch.Success)
            {
                name = nameMatch.Groups[1].Value.Trim();
            }

            var descMatch = Regex.Match(frontmatter, @"description:\s*(.+)", RegexOptions.Singleline);
            if (descMatch.Success)
            {
                description = descMatch.Groups[1].Value.Trim();
            }
        }

        // Extract system prompt (everything after frontmatter)
        var systemPrompt = content;
        if (frontmatterMatch.Success)
        {
            systemPrompt = content.Substring(frontmatterMatch.Index + frontmatterMatch.Length).Trim();
        }

        return (name, description, systemPrompt);
    }

    /// <summary>
    /// Build a review prompt tailored for this specific agent.
    /// </summary>
    private string BuildAgentReviewPrompt(
        string agentName,
        string description,
        string systemPrompt,
        ReviewRequest request,
        List<FileDiff> diffs)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"You are consulting as part of the 'Council of Greybeards' - a panel of expert agents reviewing a pull request.");
        sb.AppendLine();
        sb.AppendLine($"**Your role**: {agentName}");
        sb.AppendLine($"**Your expertise**: {description}");
        sb.AppendLine();
        sb.AppendLine("**Task**: Review the following PR changes from your area of expertise.");
        sb.AppendLine();
        sb.AppendLine("**IMPORTANT**: If this PR is not relevant to your domain of expertise, respond with:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"relevant\": false,");
        sb.AppendLine("  \"reason\": \"Brief explanation why this PR doesn't require my expertise\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If the PR **is** relevant to your expertise, provide a detailed review:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"relevant\": true,");
        sb.AppendLine("  \"summary\": \"Your overall assessment from your expert perspective\",");
        sb.AppendLine("  \"comments\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"path\": \"src/Example.cs\",");
        sb.AppendLine("      \"line\": 42,");
        sb.AppendLine("      \"severity\": \"critical|high|medium|low|info\",");
        sb.AppendLine("      \"body\": \"Your expert opinion on this specific issue\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## PR Details");
        sb.AppendLine();
        sb.AppendLine($"**Title**: {request.PullRequestTitle}");
        sb.AppendLine($"**Description**: {request.PullRequestBody ?? "(no description)"}");
        sb.AppendLine($"**Repository**: {request.Owner}/{request.Repository}");
        sb.AppendLine();
        sb.AppendLine("## Changed Files");
        sb.AppendLine();

        foreach (var diff in diffs)
        {
            sb.AppendLine($"### {diff.FileName} ({diff.Status})");
            sb.AppendLine($"Changes: +{diff.Additions} -{diff.Deletions}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(diff.Patch))
            {
                sb.AppendLine("```diff");
                sb.AppendLine(diff.Patch);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parse agent response to extract relevance and opinion.
    /// </summary>
    private CouncilMemberOpinion ParseAgentResponse(string agentName, string response)
    {
        try
        {
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = System.Text.Json.JsonDocument.Parse(json);

                var root = parsed.RootElement;
                var isRelevant = root.TryGetProperty("relevant", out var relevantProp) && relevantProp.GetBoolean();

                var opinion = new CouncilMemberOpinion
                {
                    AgentName = agentName,
                    IsRelevant = isRelevant
                };

                if (!isRelevant)
                {
                    opinion.Opinion = root.TryGetProperty("reason", out var reasonProp)
                        ? reasonProp.GetString() ?? "No comment"
                        : "No comment";
                }
                else
                {
                    opinion.Opinion = root.TryGetProperty("summary", out var summaryProp)
                        ? summaryProp.GetString() ?? ""
                        : "";

                    if (root.TryGetProperty("comments", out var commentsProp) && commentsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var commentElement in commentsProp.EnumerateArray())
                        {
                            opinion.Comments.Add(new CouncilComment
                            {
                                Path = commentElement.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "",
                                Line = commentElement.TryGetProperty("line", out var lineProp) ? lineProp.GetInt32() : 0,
                                Severity = commentElement.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "info" : "info",
                                Body = commentElement.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : ""
                            });
                        }
                    }
                }

                return opinion;
            }

            // Fallback: couldn't parse JSON
            return new CouncilMemberOpinion
            {
                AgentName = agentName,
                IsRelevant = false,
                Opinion = "Could not parse agent response"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing response from {Agent}", agentName);
            return new CouncilMemberOpinion
            {
                AgentName = agentName,
                IsRelevant = false,
                Opinion = $"Error parsing response: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Opinion from a single council member (agent).
/// </summary>
public class CouncilMemberOpinion
{
    public string AgentName { get; set; } = "";
    public bool IsRelevant { get; set; }
    public string Opinion { get; set; } = "";
    public List<CouncilComment> Comments { get; set; } = new();
}

/// <summary>
/// A comment from a council member on a specific file/line.
/// </summary>
public class CouncilComment
{
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public string Severity { get; set; } = "info";
    public string Body { get; set; } = "";
}
