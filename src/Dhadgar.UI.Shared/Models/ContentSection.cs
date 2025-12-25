namespace Dhadgar.UI.Shared.Models;

public sealed class ContentSection
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public string? Markdown { get; set; }
}
