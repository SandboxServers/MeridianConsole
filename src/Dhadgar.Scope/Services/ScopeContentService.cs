namespace Dhadgar.Scope.Services;

public sealed class ScopeContentService
{
    public Task<IReadOnlyList<ScopeSectionInfo>> GetSectionsAsync()
        => Task.FromResult(ScopeSectionsRegistry.All);
}
