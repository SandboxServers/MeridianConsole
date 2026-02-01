namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Request to update a node's properties.
/// </summary>
/// <param name="Name">New machine-readable name (lowercase alphanumeric with hyphens).</param>
/// <param name="DisplayName">New human-friendly display name.</param>
public record UpdateNodeRequest(
    string? Name,
    string? DisplayName);

/// <summary>
/// Request to update a node's tags.
/// </summary>
/// <param name="Tags">List of tags to assign. Tags are normalized to lowercase.</param>
public record UpdateNodeTagsRequest(
    IReadOnlyList<string> Tags);
