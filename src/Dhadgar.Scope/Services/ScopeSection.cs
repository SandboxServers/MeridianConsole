namespace Dhadgar.Scope.Services;

public sealed record ScopeSectionInfo(
    int Number,
    string Title,
    string Slug,
    Type ComponentType
);
