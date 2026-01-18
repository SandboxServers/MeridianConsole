namespace Dhadgar.Identity.Authorization;

public sealed class RoleDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> ImpliedClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CanAssignRoles { get; init; } = Array.Empty<string>();
}

public static class RoleDefinitions
{
    public static readonly IReadOnlyDictionary<string, RoleDefinition> Roles =
        new Dictionary<string, RoleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner"] = new RoleDefinition
            {
                Name = "Owner",
                Description = "Full control over organization",
                ImpliedClaims = new[]
                {
                    "org:read", "org:write", "org:delete", "org:billing",
                    "members:read", "members:invite", "members:remove", "members:roles",
                    "servers:read", "servers:write", "servers:delete",
                    "servers:start", "servers:stop", "servers:restart",
                    "nodes:read", "nodes:manage",
                    "files:read", "files:write", "files:delete",
                    "mods:read", "mods:write", "mods:delete",
                    // Secrets permissions (platform secrets)
                    "secrets:read:oauth",
                    "secrets:read:infrastructure",
                },
                CanAssignRoles = new[] { "admin", "operator", "viewer" }
            },
            ["admin"] = new RoleDefinition
            {
                Name = "Administrator",
                Description = "Manage servers and members",
                ImpliedClaims = new[]
                {
                    "org:read",
                    "members:read", "members:invite", "members:remove",
                    "servers:read", "servers:write", "servers:delete",
                    "servers:start", "servers:stop", "servers:restart",
                    "nodes:read",
                    "files:read", "files:write", "files:delete",
                    "mods:read", "mods:write", "mods:delete",
                    // Secrets permissions (platform secrets - read only)
                    "secrets:read:oauth",
                },
                CanAssignRoles = new[] { "operator", "viewer" }
            },
            ["operator"] = new RoleDefinition
            {
                Name = "Operator",
                Description = "Operate servers and manage files",
                ImpliedClaims = new[]
                {
                    "org:read",
                    "members:read",
                    "servers:read", "servers:write",
                    "servers:start", "servers:stop", "servers:restart",
                    "nodes:read",
                    "files:read", "files:write",
                    "mods:read", "mods:write",
                },
                CanAssignRoles = Array.Empty<string>()
            },
            ["viewer"] = new RoleDefinition
            {
                Name = "Viewer",
                Description = "Read-only access",
                ImpliedClaims = new[]
                {
                    "org:read",
                    "members:read",
                    "servers:read",
                    "nodes:read",
                    "files:read",
                    "mods:read",
                    // No secrets access for viewers
                },
                CanAssignRoles = Array.Empty<string>()
            },

            // Platform-level roles (assigned by platform admins, not org owners)
            ["platform-admin"] = new RoleDefinition
            {
                Name = "Platform Administrator",
                Description = "Full platform administration including secrets management",
                ImpliedClaims = new[]
                {
                    // Full secrets access
                    "secrets:*",
                    "secrets:read:*",
                    "secrets:write:*",
                    "secrets:rotate:*",
                    // Can approve break-glass requests
                    "break-glass:approve",
                },
                CanAssignRoles = new[] { "secrets-admin", "secrets-reader" }
            },
            ["secrets-admin"] = new RoleDefinition
            {
                Name = "Secrets Administrator",
                Description = "Full control over platform secrets",
                ImpliedClaims = new[]
                {
                    "secrets:read:*",
                    "secrets:write:*",
                    "secrets:rotate:*",
                },
                CanAssignRoles = new[] { "secrets-reader" }
            },
            ["secrets-reader"] = new RoleDefinition
            {
                Name = "Secrets Reader",
                Description = "Read-only access to platform secrets",
                ImpliedClaims = new[]
                {
                    "secrets:read:oauth",
                    "secrets:read:betterauth",
                    "secrets:read:infrastructure",
                },
                CanAssignRoles = Array.Empty<string>()
            }
        };

    public static RoleDefinition GetRole(string roleName)
    {
        return Roles.TryGetValue(roleName, out var role)
            ? role
            : Roles["viewer"];
    }

    public static bool IsValidRole(string roleName) => Roles.ContainsKey(roleName);

    public static bool CanAssignRole(string assignerRole, string targetRole)
    {
        var definition = GetRole(assignerRole);
        return definition.CanAssignRoles.Contains(targetRole, StringComparer.OrdinalIgnoreCase);
    }
}
