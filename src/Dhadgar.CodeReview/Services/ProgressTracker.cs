using System.Text;

namespace Dhadgar.CodeReview.Services;

/// <summary>
/// Tracks review progress and updates GitHub comment with status.
/// </summary>
public class ProgressTracker
{
    private readonly GitHubService _githubService;
    private readonly ILogger<ProgressTracker> _logger;
    private readonly string _owner;
    private readonly string _repo;
    private readonly long _commentId;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private int _totalChunks;
    private int _currentChunk;
    private int _councilMembersTotal;
    private int _councilMembersProcessed;
    private string? _currentCouncilMember;
    private int _currentCouncilMemberChunks;
    private int _currentCouncilMemberChunkProgress;

    public ProgressTracker(
        GitHubService githubService,
        ILogger<ProgressTracker> logger,
        string owner,
        string repo,
        long commentId)
    {
        _githubService = githubService;
        _logger = logger;
        _owner = owner;
        _repo = repo;
        _commentId = commentId;
    }

    public async Task SetMainReviewChunksAsync(int totalChunks, CancellationToken cancellationToken = default)
    {
        _totalChunks = totalChunks;
        _currentChunk = 0;
        await UpdateProgressAsync(cancellationToken);
    }

    public async Task UpdateMainReviewChunkAsync(int chunkNumber, CancellationToken cancellationToken = default)
    {
        _currentChunk = chunkNumber;
        await UpdateProgressAsync(cancellationToken);
    }

    public async Task SetCouncilMembersAsync(int total, CancellationToken cancellationToken = default)
    {
        _councilMembersTotal = total;
        _councilMembersProcessed = 0;
        await UpdateProgressAsync(cancellationToken);
    }

    public async Task StartCouncilMemberAsync(string memberName, int totalChunks, CancellationToken cancellationToken = default)
    {
        _currentCouncilMember = memberName;
        _currentCouncilMemberChunks = totalChunks;
        _currentCouncilMemberChunkProgress = 0;
        await UpdateProgressAsync(cancellationToken);
    }

    public async Task UpdateCouncilMemberChunkAsync(int chunkNumber, CancellationToken cancellationToken = default)
    {
        _currentCouncilMemberChunkProgress = chunkNumber;
        await UpdateProgressAsync(cancellationToken);
    }

    public async Task CompleteCouncilMemberAsync(CancellationToken cancellationToken = default)
    {
        _councilMembersProcessed++;
        _currentCouncilMember = null;
        _currentCouncilMemberChunks = 0;
        _currentCouncilMemberChunkProgress = 0;
        await UpdateProgressAsync(cancellationToken);
    }

    private async Task UpdateProgressAsync(CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            var status = BuildProgressMessage();
            await _githubService.UpdateCommentAsync(_owner, _repo, _commentId, status, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update progress comment");
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private string BuildProgressMessage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("🚀 **Review in progress...**");
        sb.AppendLine();

        // Main review progress
        if (_totalChunks > 1)
        {
            sb.AppendLine($"**Main Review:** Processing chunk {_currentChunk}/{_totalChunks}");
        }
        else if (_totalChunks == 1 || _currentChunk > 0)
        {
            sb.AppendLine("**Main Review:** Analyzing changes...");
        }

        // Council progress
        if (_councilMembersTotal > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Council of Greybeards:** {_councilMembersProcessed}/{_councilMembersTotal} members consulted");

            if (!string.IsNullOrEmpty(_currentCouncilMember))
            {
                if (_currentCouncilMemberChunks > 1)
                {
                    sb.AppendLine($"- Consulting **{_currentCouncilMember}** (chunk {_currentCouncilMemberChunkProgress}/{_currentCouncilMemberChunks})");
                }
                else
                {
                    sb.AppendLine($"- Consulting **{_currentCouncilMember}**...");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("_This comment will be updated with the final review when complete._");

        return sb.ToString();
    }
}
