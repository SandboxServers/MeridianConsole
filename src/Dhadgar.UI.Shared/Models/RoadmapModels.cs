namespace Dhadgar.UI.Shared.Models;

public sealed class RoadmapData
{
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string CriticalPathNote { get; set; } = string.Empty;
    public List<RoadmapPhaseSummary> PhaseSummaries { get; set; } = new();
    public List<RoadmapPhase> Phases { get; set; } = new();
}

public sealed class RoadmapPhaseSummary
{
    public string Label { get; set; } = string.Empty;
    public string Count { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "Primary";
}

public sealed class RoadmapPhase
{
    public string Name { get; set; } = string.Empty;
    public string Timeline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "Primary";
    public List<string> Goals { get; set; } = new();
    public List<RoadmapMilestone> Milestones { get; set; } = new();
}

public sealed class RoadmapMilestone
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Dependencies { get; set; } = string.Empty;
    public List<string> SuccessCriteria { get; set; } = new();
}
