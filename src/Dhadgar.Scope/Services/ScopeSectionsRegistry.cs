using Dhadgar.Scope.Pages.Sections;

namespace Dhadgar.Scope.Services;

public static class ScopeSectionsRegistry
{
    public static readonly IReadOnlyList<ScopeSectionInfo> All = new List<ScopeSectionInfo>
    {
        new ScopeSectionInfo(1, "Project Structure", "project-structure", typeof(Section01ProjectStructure)),
        new ScopeSectionInfo(2, "Vision & Scope", "vision", typeof(Section02Vision)),
        new ScopeSectionInfo(3, "Build Strategy", "build-strategy", typeof(Section03BuildStrategy)),
        new ScopeSectionInfo(4, "Tech Stack", "tech-stack", typeof(Section04TechStack)),
        new ScopeSectionInfo(5, "Deployment Topology", "deployment", typeof(Section05Deployment)),
        new ScopeSectionInfo(6, "Data Retention", "data-retention", typeof(Section06DataRetention)),
        new ScopeSectionInfo(7, "Security Architecture", "security", typeof(Section07Security)),
        new ScopeSectionInfo(8, "Certificate Management", "certificates", typeof(Section08Certificates)),
        new ScopeSectionInfo(9, "System Architecture", "architecture", typeof(Section09Architecture)),
        new ScopeSectionInfo(10, "Network Flows", "flows", typeof(Section10Flows)),
        new ScopeSectionInfo(11, "Service Communication Matrix", "matrix", typeof(Section11Matrix)),
        new ScopeSectionInfo(12, "Rabbitmq Topology", "rabbitmq", typeof(Section12Rabbitmq)),
        new ScopeSectionInfo(13, "Services", "services", typeof(Section13Services)),
        new ScopeSectionInfo(14, "Agents", "agents", typeof(Section14Agents)),
        new ScopeSectionInfo(15, "KiP EDITION", "kip", typeof(Section15Kip)),
        new ScopeSectionInfo(16, "Mvp Scope & Phases - Detailed Roadmap", "mvp", typeof(Section16Mvp)),
        new ScopeSectionInfo(17, "Governance", "governance", typeof(Section17Governance)),
        new ScopeSectionInfo(18, "Repository Structure", "repos", typeof(Section18Repos)),
    };

    public static ScopeSectionInfo? FindBySlug(string slug)
        => All.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
