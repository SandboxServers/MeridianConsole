namespace Dhadgar.UI.Shared.Models;

public sealed class GovernanceData
{
    public List<ServiceOwnershipTableData> ServiceOwnershipTables { get; set; } = new();
    public List<DecisionCategory> DecisionCategories { get; set; } = new();
    public List<ContentSection> Sections { get; set; } = new();
}

public sealed class ServiceOwnershipTableData
{
    public string Title { get; set; } = string.Empty;
    public Dictionary<string, string> Services { get; set; } = new();
}

public sealed class DecisionCategory
{
    public string Name { get; set; } = string.Empty;
    public List<DecisionRule> Rules { get; set; } = new();
}

public sealed class DecisionRule
{
    public string Decision { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
}
