namespace Dhadgar.UI.Shared.Models;

public sealed class IndexData
{
    public HeroData Hero { get; set; } = new();
    public List<FeatureCardData> Highlights { get; set; } = new();
    public List<ContentSection> Sections { get; set; } = new();
}

public sealed class HeroData
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Callouts { get; set; } = new();
    public List<NavLinkData> Links { get; set; } = new();
}

public sealed class NavLinkData
{
    public string Title { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
}
