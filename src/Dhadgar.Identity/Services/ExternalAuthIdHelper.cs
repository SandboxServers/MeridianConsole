namespace Dhadgar.Identity.Services;

public static class ExternalAuthIdHelper
{
    public const string ManualPrefix = "manual:";

    public static string CreateManualId(Guid userId)
        => $"{ManualPrefix}{userId:D}";

    public static bool IsManualId(string? externalAuthId)
        => !string.IsNullOrWhiteSpace(externalAuthId) &&
           externalAuthId.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase);
}
