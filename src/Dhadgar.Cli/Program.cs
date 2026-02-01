using System.CommandLine;
using System.CommandLine.Invocation;
using Dhadgar.Cli.Commands.Auth;
using Dhadgar.Cli.Commands.Discord;
using Dhadgar.Cli.Commands.Help;
using Dhadgar.Cli.Commands.Gateway;
using Dhadgar.Cli.Commands.Member;
using Dhadgar.Cli.Commands.Notifications;
using Dhadgar.Cli.Commands.Secret;
using Dhadgar.Cli.Commands.KeyVault;
using Dhadgar.Cli.Commands.Version;
using AuthStatusCommand = Dhadgar.Cli.Commands.Auth.StatusCommand;
using DiscordStatusCommand = Dhadgar.Cli.Commands.Discord.StatusCommand;
using Spectre.Console;
using IdentityListOrgsCommand = Dhadgar.Cli.Commands.Identity.ListOrgsCommand;
using IdentityGetOrgCommand = Dhadgar.Cli.Commands.Identity.GetOrgCommand;
using IdentityCreateOrgCommand = Dhadgar.Cli.Commands.Identity.CreateOrgCommand;
using IdentityUpdateOrgCommand = Dhadgar.Cli.Commands.Identity.UpdateOrgCommand;
using IdentityDeleteOrgCommand = Dhadgar.Cli.Commands.Identity.DeleteOrgCommand;
using IdentitySwitchOrgCommand = Dhadgar.Cli.Commands.Identity.SwitchOrgCommand;
using IdentitySearchOrgsCommand = Dhadgar.Cli.Commands.Identity.SearchOrgsCommand;
using IdentityListUsersCommand = Dhadgar.Cli.Commands.Identity.ListUsersCommand;
using IdentityGetUserCommand = Dhadgar.Cli.Commands.Identity.GetUserCommand;
using IdentityCreateUserCommand = Dhadgar.Cli.Commands.Identity.CreateUserCommand;
using IdentityUpdateUserCommand = Dhadgar.Cli.Commands.Identity.UpdateUserCommand;
using IdentityDeleteUserCommand = Dhadgar.Cli.Commands.Identity.DeleteUserCommand;
using IdentitySearchUsersCommand = Dhadgar.Cli.Commands.Identity.SearchUsersCommand;
using IdentityListRolesCommand = Dhadgar.Cli.Commands.Identity.ListRolesCommand;
using IdentityGetRoleCommand = Dhadgar.Cli.Commands.Identity.GetRoleCommand;
using IdentityCreateRoleCommand = Dhadgar.Cli.Commands.Identity.CreateRoleCommand;
using IdentityUpdateRoleCommand = Dhadgar.Cli.Commands.Identity.UpdateRoleCommand;
using IdentityDeleteRoleCommand = Dhadgar.Cli.Commands.Identity.DeleteRoleCommand;
using IdentityListRoleMembersCommand = Dhadgar.Cli.Commands.Identity.ListRoleMembersCommand;
using IdentityAssignRoleCommand = Dhadgar.Cli.Commands.Identity.AssignRoleCommand;
using IdentityRevokeRoleCommand = Dhadgar.Cli.Commands.Identity.RevokeRoleCommand;
using IdentitySearchRolesCommand = Dhadgar.Cli.Commands.Identity.SearchRolesCommand;
using GrantClaimCommand = Dhadgar.Cli.Commands.Identity.GrantClaimCommand;
using RevokeClaimCommand = Dhadgar.Cli.Commands.Identity.RevokeClaimCommand;
using ListClaimsCommand = Dhadgar.Cli.Commands.Identity.ListClaimsCommand;
using Dhadgar.Cli.Commands.Me;
using NodesListNodesCommand = Dhadgar.Cli.Commands.Nodes.ListNodesCommand;
using NodesGetNodeCommand = Dhadgar.Cli.Commands.Nodes.GetNodeCommand;
using NodesUpdateNodeCommand = Dhadgar.Cli.Commands.Nodes.UpdateNodeCommand;
using NodesDecommissionNodeCommand = Dhadgar.Cli.Commands.Nodes.DecommissionNodeCommand;
using NodesEnterMaintenanceCommand = Dhadgar.Cli.Commands.Nodes.EnterMaintenanceCommand;
using NodesExitMaintenanceCommand = Dhadgar.Cli.Commands.Nodes.ExitMaintenanceCommand;
using NodesCreateTokenCommand = Dhadgar.Cli.Commands.Nodes.CreateTokenCommand;
using NodesListTokensCommand = Dhadgar.Cli.Commands.Nodes.ListTokensCommand;
using NodesRevokeTokenCommand = Dhadgar.Cli.Commands.Nodes.RevokeTokenCommand;

var root = new RootCommand("Dhadgar CLI — Beautiful command-line interface for Meridian Console");

root.SetHandler(() =>
{
    AnsiConsole.Write(
        new FigletText("DHADGAR")
            .Centered()
            .Color(Color.Blue));

    AnsiConsole.MarkupLine("[dim]Meridian Console CLI — Game Server Control Plane[/]\n");

    var table = new Table()
        .Border(TableBorder.None)
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    table.AddRow("[cyan]dhadgar auth[/]", "[dim]Authentication commands (login, status, logout)[/]");
    table.AddRow("[cyan]dhadgar me[/]", "[dim]Self-service (profile, sessions, permissions)[/]");
    table.AddRow("[cyan]dhadgar identity[/]", "[dim]Identity service commands (orgs/users/roles)[/]");
    table.AddRow("[cyan]dhadgar member[/]", "[dim]Member management (list, invite, remove)[/]");
    table.AddRow("[cyan]dhadgar secret[/]", "[dim]Secret management (get, set, rotate, certificates)[/]");
    table.AddRow("[cyan]dhadgar keyvault[/]", "[dim]Azure Key Vault management (list, create)[/]");
    table.AddRow("[cyan]dhadgar gateway[/]", "[dim]Gateway diagnostics (health check)[/]");
    table.AddRow("[cyan]dhadgar nodes[/]", "[dim]Node management (list, get, maintenance)[/]");
    table.AddRow("[cyan]dhadgar enrollment[/]", "[dim]Agent enrollment tokens (create, list, revoke)[/]");
    table.AddRow("[cyan]dhadgar notifications[/]", "[dim]Notification service commands (logs, test)[/]");
    table.AddRow("[cyan]dhadgar discord[/]", "[dim]Discord service commands (status, channels)[/]");
    table.AddRow("[cyan]dhadgar commands[/]", "[dim]List available commands and usage[/]");
    table.AddRow("[cyan]dhadgar version[/]", "[dim]Show CLI build and breaking change info[/]");

    AnsiConsole.Write(table);

    AnsiConsole.MarkupLine("\n[dim]Use [cyan]dhadgar <command> --help[/] for more information[/]");
});

// ============================================================================
// AUTH COMMANDS
// ============================================================================

var authCmd = new Command("auth", "Authentication and token management");

var authLoginCmd = new Command("login", "Authenticate with the Identity service");
var clientIdOpt = new Option<string?>("--client-id", "Client ID (defaults to dev-client)");
var clientSecretOpt = new Option<string?>("--client-secret", "Client secret (defaults to dev-secret)");
var identityUrlOpt = new Option<Uri?>("--identity-url", "Identity service URL");
authLoginCmd.AddOption(clientIdOpt);
authLoginCmd.AddOption(clientSecretOpt);
authLoginCmd.AddOption(identityUrlOpt);
authLoginCmd.SetHandler(async (string? clientId, string? clientSecret, Uri? identityUrl) =>
{
    await LoginCommand.ExecuteAsync(clientId, clientSecret, identityUrl, CancellationToken.None);
}, clientIdOpt, clientSecretOpt, identityUrlOpt);

var authStatusCmd = new Command("status", "Show authentication status and configuration");
authStatusCmd.SetHandler(async () =>
{
    await AuthStatusCommand.ExecuteAsync(CancellationToken.None);
});

var authLogoutCmd = new Command("logout", "Clear authentication tokens and log out");
authLogoutCmd.SetHandler(async () =>
{
    await LogoutCommand.ExecuteAsync(CancellationToken.None);
});

authCmd.AddCommand(authLoginCmd);
authCmd.AddCommand(authStatusCmd);
authCmd.AddCommand(authLogoutCmd);

// ============================================================================
// IDENTITY COMMANDS
// ============================================================================

var identityCmd = new Command("identity", "Identity service commands");
var identityOrgsCmd = new Command("orgs", "Organization management");

var identityOrgsListCmd = new Command("list", "List organizations");
identityOrgsListCmd.SetHandler(async () =>
{
    await IdentityListOrgsCommand.ExecuteAsync(CancellationToken.None);
});

var identityOrgsGetCmd = new Command("get", "Get an organization by id");
var identityOrgIdArg = new Argument<string>("org-id", "Organization ID");
identityOrgsGetCmd.AddArgument(identityOrgIdArg);
identityOrgsGetCmd.SetHandler(async (string orgId) =>
{
    await IdentityGetOrgCommand.ExecuteAsync(orgId, CancellationToken.None);
}, identityOrgIdArg);

var identityOrgsCreateCmd = new Command("create", "Create an organization");
var identityOrgNameOpt = new Option<string>("--name", "Organization name") { IsRequired = true };
var identityOrgDescriptionOpt = new Option<string?>("--description", "Organization description");
identityOrgsCreateCmd.AddOption(identityOrgNameOpt);
identityOrgsCreateCmd.AddOption(identityOrgDescriptionOpt);
identityOrgsCreateCmd.SetHandler(async (string name, string? description) =>
{
    await IdentityCreateOrgCommand.ExecuteAsync(name, description, CancellationToken.None);
}, identityOrgNameOpt, identityOrgDescriptionOpt);

var identityOrgsUpdateCmd = new Command("update", "Update an organization");
var identityUpdateOrgIdArg = new Argument<string>("org-id", "Organization ID");
var identityUpdateNameOpt = new Option<string?>("--name", "Organization name");
var identityUpdateDescriptionOpt = new Option<string?>("--description", "Organization description");
identityOrgsUpdateCmd.AddArgument(identityUpdateOrgIdArg);
identityOrgsUpdateCmd.AddOption(identityUpdateNameOpt);
identityOrgsUpdateCmd.AddOption(identityUpdateDescriptionOpt);
identityOrgsUpdateCmd.SetHandler(async (string orgId, string? name, string? description) =>
{
    await IdentityUpdateOrgCommand.ExecuteAsync(orgId, name, description, CancellationToken.None);
}, identityUpdateOrgIdArg, identityUpdateNameOpt, identityUpdateDescriptionOpt);

var identityOrgsDeleteCmd = new Command("delete", "Delete an organization");
var identityDeleteOrgIdArg = new Argument<string>("org-id", "Organization ID");
var identityDeleteForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
identityOrgsDeleteCmd.AddArgument(identityDeleteOrgIdArg);
identityOrgsDeleteCmd.AddOption(identityDeleteForceOpt);
identityOrgsDeleteCmd.SetHandler(async (string orgId, bool force) =>
{
    await IdentityDeleteOrgCommand.ExecuteAsync(orgId, force, CancellationToken.None);
}, identityDeleteOrgIdArg, identityDeleteForceOpt);

var identityOrgsSwitchCmd = new Command("switch", "Switch to an organization");
var identitySwitchOrgIdArg = new Argument<string>("org-id", "Organization ID");
identityOrgsSwitchCmd.AddArgument(identitySwitchOrgIdArg);
identityOrgsSwitchCmd.SetHandler(async (string orgId) =>
{
    await IdentitySwitchOrgCommand.ExecuteAsync(orgId, CancellationToken.None);
}, identitySwitchOrgIdArg);

var identityOrgsSearchCmd = new Command("search", "Search organizations");
var identityOrgsQueryOpt = new Option<string>("--query", "Search query") { IsRequired = true };
identityOrgsSearchCmd.AddOption(identityOrgsQueryOpt);
identityOrgsSearchCmd.SetHandler(async (string query) =>
{
    await IdentitySearchOrgsCommand.ExecuteAsync(query, CancellationToken.None);
}, identityOrgsQueryOpt);

identityOrgsCmd.AddCommand(identityOrgsListCmd);
identityOrgsCmd.AddCommand(identityOrgsGetCmd);
identityOrgsCmd.AddCommand(identityOrgsCreateCmd);
identityOrgsCmd.AddCommand(identityOrgsUpdateCmd);
identityOrgsCmd.AddCommand(identityOrgsDeleteCmd);
identityOrgsCmd.AddCommand(identityOrgsSwitchCmd);
identityOrgsCmd.AddCommand(identityOrgsSearchCmd);

identityCmd.AddCommand(identityOrgsCmd);

// ========================================================================
// IDENTITY USERS COMMANDS
// ========================================================================

var identityUsersCmd = new Command("users", "User management");

var identityUsersListCmd = new Command("list", "List users");
var identityUsersListOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityUsersListCmd.AddOption(identityUsersListOrgOpt);
identityUsersListCmd.SetHandler(async (string? orgId) =>
{
    await IdentityListUsersCommand.ExecuteAsync(orgId, CancellationToken.None);
}, identityUsersListOrgOpt);

var identityUsersGetCmd = new Command("get", "Get a user by id");
var identityUserIdArg = new Argument<string>("user-id", "User ID");
var identityUsersGetOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityUsersGetCmd.AddArgument(identityUserIdArg);
identityUsersGetCmd.AddOption(identityUsersGetOrgOpt);
identityUsersGetCmd.SetHandler(async (string userId, string? orgId) =>
{
    await IdentityGetUserCommand.ExecuteAsync(userId, orgId, CancellationToken.None);
}, identityUserIdArg, identityUsersGetOrgOpt);

var identityUsersCreateCmd = new Command("create", "Create a user");
var identityUserEmailOpt = new Option<string>("--email", "User email") { IsRequired = true };
var identityUserOrgOpt = new Option<string>("--org", "Organization ID") { IsRequired = true };
var identityUserNameOpt = new Option<string?>("--name", "Display name");
identityUsersCreateCmd.AddOption(identityUserEmailOpt);
identityUsersCreateCmd.AddOption(identityUserOrgOpt);
identityUsersCreateCmd.AddOption(identityUserNameOpt);
identityUsersCreateCmd.SetHandler(async (string email, string orgId, string? name) =>
{
    await IdentityCreateUserCommand.ExecuteAsync(email, orgId, name, CancellationToken.None);
}, identityUserEmailOpt, identityUserOrgOpt, identityUserNameOpt);

var identityUsersUpdateCmd = new Command("update", "Update a user");
var identityUsersUpdateIdArg = new Argument<string>("user-id", "User ID");
var identityUsersUpdateEmailOpt = new Option<string?>("--email", "User email");
var identityUsersUpdateNameOpt = new Option<string?>("--name", "Display name");
var identityUsersUpdateOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityUsersUpdateCmd.AddArgument(identityUsersUpdateIdArg);
identityUsersUpdateCmd.AddOption(identityUsersUpdateEmailOpt);
identityUsersUpdateCmd.AddOption(identityUsersUpdateNameOpt);
identityUsersUpdateCmd.AddOption(identityUsersUpdateOrgOpt);
identityUsersUpdateCmd.SetHandler(async (string userId, string? email, string? name, string? orgId) =>
{
    await IdentityUpdateUserCommand.ExecuteAsync(userId, email, name, orgId, CancellationToken.None);
}, identityUsersUpdateIdArg, identityUsersUpdateEmailOpt, identityUsersUpdateNameOpt, identityUsersUpdateOrgOpt);

var identityUsersDeleteCmd = new Command("delete", "Delete a user");
var identityUsersDeleteIdArg = new Argument<string>("user-id", "User ID");
var identityUsersDeleteForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
var identityUsersDeleteOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityUsersDeleteCmd.AddArgument(identityUsersDeleteIdArg);
identityUsersDeleteCmd.AddOption(identityUsersDeleteForceOpt);
identityUsersDeleteCmd.AddOption(identityUsersDeleteOrgOpt);
identityUsersDeleteCmd.SetHandler(async (string userId, bool force, string? orgId) =>
{
    await IdentityDeleteUserCommand.ExecuteAsync(userId, orgId, force, CancellationToken.None);
}, identityUsersDeleteIdArg, identityUsersDeleteForceOpt, identityUsersDeleteOrgOpt);

var identityUsersSearchCmd = new Command("search", "Search users");
var identityUsersSearchQueryOpt = new Option<string>("--query", "Search query") { IsRequired = true };
var identityUsersSearchOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityUsersSearchCmd.AddOption(identityUsersSearchQueryOpt);
identityUsersSearchCmd.AddOption(identityUsersSearchOrgOpt);
identityUsersSearchCmd.SetHandler(async (string query, string? orgId) =>
{
    await IdentitySearchUsersCommand.ExecuteAsync(query, orgId, CancellationToken.None);
}, identityUsersSearchQueryOpt, identityUsersSearchOrgOpt);

identityUsersCmd.AddCommand(identityUsersListCmd);
identityUsersCmd.AddCommand(identityUsersGetCmd);
identityUsersCmd.AddCommand(identityUsersCreateCmd);
identityUsersCmd.AddCommand(identityUsersUpdateCmd);
identityUsersCmd.AddCommand(identityUsersDeleteCmd);
identityUsersCmd.AddCommand(identityUsersSearchCmd);

identityCmd.AddCommand(identityUsersCmd);

// ========================================================================
// IDENTITY ROLES COMMANDS
// ========================================================================

var identityRolesCmd = new Command("roles", "Role management");

var identityRolesListCmd = new Command("list", "List roles");
var identityRolesListOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesListCmd.AddOption(identityRolesListOrgOpt);
identityRolesListCmd.SetHandler(async (string? orgId) =>
{
    await IdentityListRolesCommand.ExecuteAsync(orgId, CancellationToken.None);
}, identityRolesListOrgOpt);

var identityRolesGetCmd = new Command("get", "Get a role by id");
var identityRoleIdArg = new Argument<string>("role-id", "Role ID");
var identityRolesGetOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesGetCmd.AddArgument(identityRoleIdArg);
identityRolesGetCmd.AddOption(identityRolesGetOrgOpt);
identityRolesGetCmd.SetHandler(async (string roleId, string? orgId) =>
{
    await IdentityGetRoleCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
}, identityRoleIdArg, identityRolesGetOrgOpt);

var identityRolesCreateCmd = new Command("create", "Create a role");
var identityRoleNameOpt = new Option<string>("--name", "Role name") { IsRequired = true };
var identityRoleOrgOpt = new Option<string>("--org", "Organization ID") { IsRequired = true };
var identityRoleDescriptionOpt = new Option<string?>("--description", "Role description");
var identityRolePermissionsOpt = new Option<string?>("--permissions", "Comma-separated permissions");
identityRolesCreateCmd.AddOption(identityRoleNameOpt);
identityRolesCreateCmd.AddOption(identityRoleOrgOpt);
identityRolesCreateCmd.AddOption(identityRoleDescriptionOpt);
identityRolesCreateCmd.AddOption(identityRolePermissionsOpt);
identityRolesCreateCmd.SetHandler(async (string name, string orgId, string? description, string? permissions) =>
{
    await IdentityCreateRoleCommand.ExecuteAsync(name, orgId, description, permissions, CancellationToken.None);
}, identityRoleNameOpt, identityRoleOrgOpt, identityRoleDescriptionOpt, identityRolePermissionsOpt);

var identityRolesUpdateCmd = new Command("update", "Update a role");
var identityRolesUpdateIdArg = new Argument<string>("role-id", "Role ID");
var identityRolesUpdateNameOpt = new Option<string?>("--name", "Role name");
var identityRolesUpdateDescriptionOpt = new Option<string?>("--description", "Role description");
var identityRolesUpdatePermissionsOpt = new Option<string?>("--permissions", "Comma-separated permissions");
var identityRolesUpdateOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesUpdateCmd.AddArgument(identityRolesUpdateIdArg);
identityRolesUpdateCmd.AddOption(identityRolesUpdateNameOpt);
identityRolesUpdateCmd.AddOption(identityRolesUpdateDescriptionOpt);
identityRolesUpdateCmd.AddOption(identityRolesUpdatePermissionsOpt);
identityRolesUpdateCmd.AddOption(identityRolesUpdateOrgOpt);
identityRolesUpdateCmd.SetHandler(async (string roleId, string? name, string? description, string? permissions, string? orgId) =>
{
    await IdentityUpdateRoleCommand.ExecuteAsync(roleId, orgId, name, description, permissions, CancellationToken.None);
}, identityRolesUpdateIdArg, identityRolesUpdateNameOpt, identityRolesUpdateDescriptionOpt, identityRolesUpdatePermissionsOpt, identityRolesUpdateOrgOpt);

var identityRolesDeleteCmd = new Command("delete", "Delete a role");
var identityRolesDeleteIdArg = new Argument<string>("role-id", "Role ID");
var identityRolesDeleteOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesDeleteCmd.AddArgument(identityRolesDeleteIdArg);
identityRolesDeleteCmd.AddOption(identityRolesDeleteOrgOpt);
identityRolesDeleteCmd.SetHandler(async (string roleId, string? orgId) =>
{
    await IdentityDeleteRoleCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
}, identityRolesDeleteIdArg, identityRolesDeleteOrgOpt);

var identityRolesMembersCmd = new Command("members", "List members with a role");
var identityRolesMembersIdArg = new Argument<string>("role-id", "Role ID");
var identityRolesMembersOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesMembersCmd.AddArgument(identityRolesMembersIdArg);
identityRolesMembersCmd.AddOption(identityRolesMembersOrgOpt);
identityRolesMembersCmd.SetHandler(async (string roleId, string? orgId) =>
{
    await IdentityListRoleMembersCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
}, identityRolesMembersIdArg, identityRolesMembersOrgOpt);

var identityRolesAssignCmd = new Command("assign", "Assign a role to a user");
var identityRolesAssignRoleArg = new Argument<string>("role-id", "Role ID");
var identityRolesAssignUserOpt = new Option<string>("--user", "User ID") { IsRequired = true };
var identityRolesAssignOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesAssignCmd.AddArgument(identityRolesAssignRoleArg);
identityRolesAssignCmd.AddOption(identityRolesAssignUserOpt);
identityRolesAssignCmd.AddOption(identityRolesAssignOrgOpt);
identityRolesAssignCmd.SetHandler(async (string roleId, string userId, string? orgId) =>
{
    await IdentityAssignRoleCommand.ExecuteAsync(roleId, userId, orgId, CancellationToken.None);
}, identityRolesAssignRoleArg, identityRolesAssignUserOpt, identityRolesAssignOrgOpt);

var identityRolesRevokeCmd = new Command("revoke", "Revoke a role from a user");
var identityRolesRevokeRoleArg = new Argument<string>("role-id", "Role ID");
var identityRolesRevokeUserOpt = new Option<string>("--user", "User ID") { IsRequired = true };
var identityRolesRevokeOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesRevokeCmd.AddArgument(identityRolesRevokeRoleArg);
identityRolesRevokeCmd.AddOption(identityRolesRevokeUserOpt);
identityRolesRevokeCmd.AddOption(identityRolesRevokeOrgOpt);
identityRolesRevokeCmd.SetHandler(async (string roleId, string userId, string? orgId) =>
{
    await IdentityRevokeRoleCommand.ExecuteAsync(roleId, userId, orgId, CancellationToken.None);
}, identityRolesRevokeRoleArg, identityRolesRevokeUserOpt, identityRolesRevokeOrgOpt);

var identityRolesSearchCmd = new Command("search", "Search roles");
var identityRolesSearchQueryOpt = new Option<string>("--query", "Search query") { IsRequired = true };
var identityRolesSearchOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityRolesSearchCmd.AddOption(identityRolesSearchQueryOpt);
identityRolesSearchCmd.AddOption(identityRolesSearchOrgOpt);
identityRolesSearchCmd.SetHandler(async (string query, string? orgId) =>
{
    await IdentitySearchRolesCommand.ExecuteAsync(query, orgId, CancellationToken.None);
}, identityRolesSearchQueryOpt, identityRolesSearchOrgOpt);

identityRolesCmd.AddCommand(identityRolesListCmd);
identityRolesCmd.AddCommand(identityRolesGetCmd);
identityRolesCmd.AddCommand(identityRolesCreateCmd);
identityRolesCmd.AddCommand(identityRolesUpdateCmd);
identityRolesCmd.AddCommand(identityRolesDeleteCmd);
identityRolesCmd.AddCommand(identityRolesMembersCmd);
identityRolesCmd.AddCommand(identityRolesAssignCmd);
identityRolesCmd.AddCommand(identityRolesRevokeCmd);
identityRolesCmd.AddCommand(identityRolesSearchCmd);

identityCmd.AddCommand(identityRolesCmd);

// ============================================================================
// IDENTITY MEMBERS COMMANDS (Claim management)
// ============================================================================

var identityMembersCmd = new Command("members", "Member claim management");

var identityMembersGrantCmd = new Command("grant", "Grant a permission to a member");
var grantMemberIdArg = new Argument<string>("member-id", "Member ID (user ID)");
var grantPermissionArg = new Argument<string>("permission", "Permission to grant (e.g., secrets:read:oauth)");
var grantOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
var grantExpiresOpt = new Option<string?>("--expires", "Expiration (e.g., 1h, 7d, 30d, or ISO 8601 date)");
identityMembersGrantCmd.AddArgument(grantMemberIdArg);
identityMembersGrantCmd.AddArgument(grantPermissionArg);
identityMembersGrantCmd.AddOption(grantOrgOpt);
identityMembersGrantCmd.AddOption(grantExpiresOpt);
identityMembersGrantCmd.SetHandler(async (string memberId, string permission, string? orgId, string? expires) =>
{
    await GrantClaimCommand.ExecuteAsync(memberId, permission, orgId, expires, CancellationToken.None);
}, grantMemberIdArg, grantPermissionArg, grantOrgOpt, grantExpiresOpt);

var identityMembersRevokeCmd = new Command("revoke", "Revoke a claim from a member");
var revokeMemberIdArg = new Argument<string>("member-id", "Member ID (user ID)");
var revokeClaimIdArg = new Argument<string>("claim-id", "Claim ID to revoke");
var revokeOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
var revokeForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
identityMembersRevokeCmd.AddArgument(revokeMemberIdArg);
identityMembersRevokeCmd.AddArgument(revokeClaimIdArg);
identityMembersRevokeCmd.AddOption(revokeOrgOpt);
identityMembersRevokeCmd.AddOption(revokeForceOpt);
identityMembersRevokeCmd.SetHandler(async (string memberId, string claimId, string? orgId, bool force) =>
{
    await RevokeClaimCommand.ExecuteAsync(memberId, claimId, orgId, force, CancellationToken.None);
}, revokeMemberIdArg, revokeClaimIdArg, revokeOrgOpt, revokeForceOpt);

var identityMembersClaimsCmd = new Command("claims", "List custom claims for a member");
var claimsMemberIdArg = new Argument<string>("member-id", "Member ID (user ID)");
var claimsOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
identityMembersClaimsCmd.AddArgument(claimsMemberIdArg);
identityMembersClaimsCmd.AddOption(claimsOrgOpt);
identityMembersClaimsCmd.SetHandler(async (string memberId, string? orgId) =>
{
    await ListClaimsCommand.ExecuteAsync(memberId, orgId, CancellationToken.None);
}, claimsMemberIdArg, claimsOrgOpt);

identityMembersCmd.AddCommand(identityMembersGrantCmd);
identityMembersCmd.AddCommand(identityMembersRevokeCmd);
identityMembersCmd.AddCommand(identityMembersClaimsCmd);

identityCmd.AddCommand(identityMembersCmd);

// ============================================================================
// ME COMMANDS (Self-service)
// ============================================================================

var meCmd = new Command("me", "Self-service commands for current user");

var meProfileCmd = new Command("profile", "Get your profile information");
meProfileCmd.SetHandler(async () =>
{
    await GetProfileCommand.ExecuteAsync(CancellationToken.None);
});

var meUpdateProfileCmd = new Command("update", "Update your profile");
var meDisplayNameOpt = new Option<string?>("--name", "Display name");
var mePreferredOrgOpt = new Option<string?>("--preferred-org", "Preferred organization ID");
meUpdateProfileCmd.AddOption(meDisplayNameOpt);
meUpdateProfileCmd.AddOption(mePreferredOrgOpt);
meUpdateProfileCmd.SetHandler(async (string? displayName, string? preferredOrgId) =>
{
    await UpdateProfileCommand.ExecuteAsync(displayName, preferredOrgId, CancellationToken.None);
}, meDisplayNameOpt, mePreferredOrgOpt);

var meOrgsCmd = new Command("orgs", "List your organizations");
meOrgsCmd.SetHandler(async () =>
{
    await ListOrganizationsCommand.ExecuteAsync(CancellationToken.None);
});

var meLinkedAccountsCmd = new Command("linked-accounts", "List your linked accounts");
meLinkedAccountsCmd.SetHandler(async () =>
{
    await ListLinkedAccountsCommand.ExecuteAsync(CancellationToken.None);
});

var mePermissionsCmd = new Command("permissions", "List your permissions");
mePermissionsCmd.SetHandler(async () =>
{
    await ListPermissionsCommand.ExecuteAsync(CancellationToken.None);
});

var meSessionsCmd = new Command("sessions", "Session management");

var meSessionsListCmd = new Command("list", "List your active sessions");
meSessionsListCmd.SetHandler(async () =>
{
    await ListSessionsCommand.ExecuteAsync(CancellationToken.None);
});

var meSessionsRevokeCmd = new Command("revoke", "Revoke a session");
var meSessionIdArg = new Argument<string>("session-id", "Session ID to revoke");
meSessionsRevokeCmd.AddArgument(meSessionIdArg);
meSessionsRevokeCmd.SetHandler(async (string sessionId) =>
{
    await RevokeSessionCommand.ExecuteAsync(sessionId, CancellationToken.None);
}, meSessionIdArg);

var meSessionsRevokeAllCmd = new Command("revoke-all", "Revoke all sessions (logout everywhere)");
meSessionsRevokeAllCmd.SetHandler(async () =>
{
    await RevokeAllSessionsCommand.ExecuteAsync(CancellationToken.None);
});

meSessionsCmd.AddCommand(meSessionsListCmd);
meSessionsCmd.AddCommand(meSessionsRevokeCmd);
meSessionsCmd.AddCommand(meSessionsRevokeAllCmd);

meCmd.AddCommand(meProfileCmd);
meCmd.AddCommand(meUpdateProfileCmd);
meCmd.AddCommand(meOrgsCmd);
meCmd.AddCommand(meLinkedAccountsCmd);
meCmd.AddCommand(mePermissionsCmd);
meCmd.AddCommand(meSessionsCmd);

// ============================================================================
// MEMBER COMMANDS
// ============================================================================

var memberCmd = new Command("member", "Organization member management");

var memberListCmd = new Command("list", "List members of an organization");
var memberOrgIdArg = new Argument<string?>("org-id", "Organization ID (uses current org if not specified)") { Arity = ArgumentArity.ZeroOrOne };
memberListCmd.AddArgument(memberOrgIdArg);
memberListCmd.SetHandler(async (string? orgId) =>
{
    await ListMembersCommand.ExecuteAsync(orgId, CancellationToken.None);
}, memberOrgIdArg);

memberCmd.AddCommand(memberListCmd);

// ============================================================================
// SECRET COMMANDS
// ============================================================================

var secretCmd = new Command("secret", "Secret management");

var secretGetCmd = new Command("get", "Get a single secret by name");
var secretNameArg = new Argument<string>("name", "Secret name");
var revealOpt = new Option<bool>("--reveal", "Reveal the actual secret value (masked by default)");
var copyOpt = new Option<bool>("--copy", "Copy secret to clipboard");
secretGetCmd.AddArgument(secretNameArg);
secretGetCmd.AddOption(revealOpt);
secretGetCmd.AddOption(copyOpt);
secretGetCmd.SetHandler(async (string name, bool reveal, bool copy) =>
{
    await GetSecretCommand.ExecuteAsync(name, reveal, copy, CancellationToken.None);
}, secretNameArg, revealOpt, copyOpt);

var secretListCmd = new Command("list", "List secrets by category");
var categoryArg = new Argument<string>("category", "Secret category (oauth, betterauth, infrastructure)");
var revealListOpt = new Option<bool>("--reveal", "Reveal actual secret values (masked by default)");
secretListCmd.AddArgument(categoryArg);
secretListCmd.AddOption(revealListOpt);
secretListCmd.SetHandler(async (string category, bool reveal) =>
{
    await ListSecretsCommand.ExecuteAsync(category, reveal, CancellationToken.None);
}, categoryArg, revealListOpt);

var secretSetCmd = new Command("set", "Set or update a secret value");
var setSecretNameArg = new Argument<string>("name", "Secret name");
var setValueArg = new Argument<string?>("value", "Secret value (optional, will prompt if not provided)") { Arity = ArgumentArity.ZeroOrOne };
var stdinOpt = new Option<bool>("--stdin", "Read secret value from stdin");
secretSetCmd.AddArgument(setSecretNameArg);
secretSetCmd.AddArgument(setValueArg);
secretSetCmd.AddOption(stdinOpt);
secretSetCmd.SetHandler(async (string name, string? value, bool stdin) =>
{
    await SetSecretCommand.ExecuteAsync(name, value, stdin, CancellationToken.None);
}, setSecretNameArg, setValueArg, stdinOpt);

var secretRotateCmd = new Command("rotate", "Rotate a secret (generate new value)");
var rotateSecretNameArg = new Argument<string>("name", "Secret name");
var forceOpt = new Option<bool>("--force", "Skip confirmation prompt");
secretRotateCmd.AddArgument(rotateSecretNameArg);
secretRotateCmd.AddOption(forceOpt);
secretRotateCmd.SetHandler(async (string name, bool force) =>
{
    await RotateSecretCommand.ExecuteAsync(name, force, CancellationToken.None);
}, rotateSecretNameArg, forceOpt);

var secretDeleteCmd = new Command("delete", "Delete a secret");
var deleteSecretNameArg = new Argument<string>("name", "Secret name");
var deleteSecretForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
secretDeleteCmd.AddArgument(deleteSecretNameArg);
secretDeleteCmd.AddOption(deleteSecretForceOpt);
secretDeleteCmd.SetHandler(async (string name, bool force) =>
{
    await DeleteSecretCommand.ExecuteAsync(name, force, CancellationToken.None);
}, deleteSecretNameArg, deleteSecretForceOpt);

var certListCmd = new Command("list-certs", "List certificates");
var certVaultNameOpt = new Option<string?>("--vault", "Key Vault name (optional)");
certListCmd.AddOption(certVaultNameOpt);
certListCmd.SetHandler(async (string? vaultName) =>
{
    await ListCertificatesCommand.ExecuteAsync(vaultName, CancellationToken.None);
}, certVaultNameOpt);

var certImportCmd = new Command("import-cert", "Import a certificate");
var certPathArg = new Argument<string>("path", "Path to certificate file (.pfx, .p12, .pem, .cer)");
var certNameOpt = new Option<string?>("--name", "Certificate name (defaults to filename)");
var certPasswordOpt = new Option<string?>("--password", "Certificate password (for PFX/P12)");
var certVaultOpt = new Option<string?>("--vault", "Key Vault name (optional)");
certImportCmd.AddArgument(certPathArg);
certImportCmd.AddOption(certNameOpt);
certImportCmd.AddOption(certPasswordOpt);
certImportCmd.AddOption(certVaultOpt);
certImportCmd.SetHandler(async (string path, string? name, string? password, string? vault) =>
{
    await ImportCertificateCommand.ExecuteAsync(path, name, password, vault, CancellationToken.None);
}, certPathArg, certNameOpt, certPasswordOpt, certVaultOpt);

secretCmd.AddCommand(secretGetCmd);
secretCmd.AddCommand(secretListCmd);
secretCmd.AddCommand(secretSetCmd);
secretCmd.AddCommand(secretRotateCmd);
secretCmd.AddCommand(secretDeleteCmd);
secretCmd.AddCommand(certListCmd);
secretCmd.AddCommand(certImportCmd);

// ============================================================================
// KEY VAULT COMMANDS
// ============================================================================

var keyvaultCmd = new Command("keyvault", "Azure Key Vault management");

var vaultListCmd = new Command("list", "List all Key Vaults");
vaultListCmd.SetHandler(async () =>
{
    await ListVaultsCommand.ExecuteAsync(CancellationToken.None);
});

var vaultCreateCmd = new Command("create", "Create a new Key Vault");
var vaultNameArg = new Argument<string?>("name", "Vault name (optional, will prompt)") { Arity = ArgumentArity.ZeroOrOne };
var vaultLocationOpt = new Option<string?>("--location", "Azure location");
vaultCreateCmd.AddArgument(vaultNameArg);
vaultCreateCmd.AddOption(vaultLocationOpt);
vaultCreateCmd.SetHandler(async (string? name, string? location) =>
{
    await CreateVaultCommand.ExecuteAsync(name, location, CancellationToken.None);
}, vaultNameArg, vaultLocationOpt);

var vaultGetCmd = new Command("get", "Get Key Vault details");
var vaultGetNameArg = new Argument<string>("name", "Vault name");
vaultGetCmd.AddArgument(vaultGetNameArg);
vaultGetCmd.SetHandler(async (string name) =>
{
    await GetVaultCommand.ExecuteAsync(name, CancellationToken.None);
}, vaultGetNameArg);

var vaultUpdateCmd = new Command("update", "Update Key Vault properties");
var vaultUpdateNameArg = new Argument<string>("name", "Vault name");
var enableSoftDeleteOpt = new Option<bool?>("--enable-soft-delete", "Enable soft delete");
var disableSoftDeleteOpt = new Option<bool?>("--disable-soft-delete", "Disable soft delete");
var enablePurgeProtectionOpt = new Option<bool?>("--enable-purge-protection", "Enable purge protection");
var disablePurgeProtectionOpt = new Option<bool?>("--disable-purge-protection", "Disable purge protection");
var retentionDaysOpt = new Option<int?>("--retention-days", "Soft delete retention days (7-90)");
var skuOpt = new Option<string?>("--sku", "Vault SKU (standard or premium)");

vaultUpdateCmd.AddArgument(vaultUpdateNameArg);
vaultUpdateCmd.AddOption(enableSoftDeleteOpt);
vaultUpdateCmd.AddOption(disableSoftDeleteOpt);
vaultUpdateCmd.AddOption(enablePurgeProtectionOpt);
vaultUpdateCmd.AddOption(disablePurgeProtectionOpt);
vaultUpdateCmd.AddOption(retentionDaysOpt);
vaultUpdateCmd.AddOption(skuOpt);

vaultUpdateCmd.SetHandler(async (
    string name,
    bool? enableSD,
    bool? disableSD,
    bool? enablePP,
    bool? disablePP,
    int? retention,
    string? sku) =>
{
    // Convert enable/disable flags to nullable bool
    bool? softDelete = enableSD.HasValue ? enableSD : (disableSD.HasValue ? !disableSD : null);
    bool? purgeProtection = enablePP.HasValue ? enablePP : (disablePP.HasValue ? !disablePP : null);

    await UpdateVaultCommand.ExecuteAsync(name, softDelete, purgeProtection, retention, sku, CancellationToken.None);
}, vaultUpdateNameArg, enableSoftDeleteOpt, disableSoftDeleteOpt, enablePurgeProtectionOpt, disablePurgeProtectionOpt, retentionDaysOpt, skuOpt);

var vaultDeleteCmd = new Command("delete", "Delete a Key Vault");
var vaultDeleteNameArg = new Argument<string>("name", "Vault name");
var vaultDeleteForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
vaultDeleteCmd.AddArgument(vaultDeleteNameArg);
vaultDeleteCmd.AddOption(vaultDeleteForceOpt);
vaultDeleteCmd.SetHandler(async (string name, bool force) =>
{
    await DeleteVaultCommand.ExecuteAsync(name, force, CancellationToken.None);
}, vaultDeleteNameArg, vaultDeleteForceOpt);

keyvaultCmd.AddCommand(vaultListCmd);
keyvaultCmd.AddCommand(vaultGetCmd);
keyvaultCmd.AddCommand(vaultCreateCmd);
keyvaultCmd.AddCommand(vaultUpdateCmd);
keyvaultCmd.AddCommand(vaultDeleteCmd);

// ============================================================================
// GATEWAY COMMANDS
// ============================================================================

var gatewayCmd = new Command("gateway", "Gateway and infrastructure diagnostics");

var gatewayHealthCmd = new Command("health", "Check health of all services");
gatewayHealthCmd.SetHandler(async (InvocationContext ctx) =>
{
    ctx.ExitCode = await HealthCommand.ExecuteAsync(ctx.GetCancellationToken());
});

var gatewayServicesCmd = new Command("services", "List all backend services health (Development only)");
gatewayServicesCmd.SetHandler(async (InvocationContext ctx) =>
{
    ctx.ExitCode = await ServicesCommand.ExecuteAsync(ctx.GetCancellationToken());
});

var gatewayRoutesCmd = new Command("routes", "List all gateway routes (Development only)");
gatewayRoutesCmd.SetHandler(async (InvocationContext ctx) =>
{
    ctx.ExitCode = await RoutesCommand.ExecuteAsync(ctx.GetCancellationToken());
});

var gatewayClustersCmd = new Command("clusters", "List all YARP clusters (Development only)");
gatewayClustersCmd.SetHandler(async (InvocationContext ctx) =>
{
    ctx.ExitCode = await ClustersCommand.ExecuteAsync(ctx.GetCancellationToken());
});

gatewayCmd.AddCommand(gatewayHealthCmd);
gatewayCmd.AddCommand(gatewayServicesCmd);
gatewayCmd.AddCommand(gatewayRoutesCmd);
gatewayCmd.AddCommand(gatewayClustersCmd);

// ============================================================================
// NODES COMMANDS
// ============================================================================

var nodesCmd = new Command("nodes", "Node management commands");

var nodesListCmd = new Command("list", "List nodes");
var nodesListOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
var nodesListStatusOpt = new Option<string?>("--status", "Filter by status (online, offline, degraded, maintenance)");
var nodesListSkipOpt = new Option<int?>("--skip", "Number of items to skip (pagination)");
nodesListSkipOpt.AddValidator(result =>
{
    var value = result.GetValueForOption(nodesListSkipOpt);
    if (value.HasValue && value.Value < 0)
    {
        result.ErrorMessage = "skip must be >= 0";
    }
});
var nodesListTakeOpt = new Option<int?>("--take", "Number of items to take (pagination)");
nodesListTakeOpt.AddValidator(result =>
{
    var value = result.GetValueForOption(nodesListTakeOpt);
    if (value.HasValue && value.Value <= 0)
    {
        result.ErrorMessage = "take must be > 0";
    }
});
nodesListCmd.AddOption(nodesListOrgOpt);
nodesListCmd.AddOption(nodesListStatusOpt);
nodesListCmd.AddOption(nodesListSkipOpt);
nodesListCmd.AddOption(nodesListTakeOpt);
nodesListCmd.SetHandler(async (string? orgId, string? status, int? skip, int? take) =>
{
    await NodesListNodesCommand.ExecuteAsync(orgId, status, skip, take, CancellationToken.None);
}, nodesListOrgOpt, nodesListStatusOpt, nodesListSkipOpt, nodesListTakeOpt);

var nodesGetCmd = new Command("get", "Get node details");
var nodesGetNodeIdArg = new Argument<string>("node-id", "Node ID");
var nodesGetOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
nodesGetCmd.AddArgument(nodesGetNodeIdArg);
nodesGetCmd.AddOption(nodesGetOrgOpt);
nodesGetCmd.SetHandler(async (string nodeId, string? orgId) =>
{
    await NodesGetNodeCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
}, nodesGetNodeIdArg, nodesGetOrgOpt);

var nodesUpdateCmd = new Command("update", "Update node properties");
var nodesUpdateNodeIdArg = new Argument<string>("node-id", "Node ID");
var nodesUpdateDisplayNameOpt = new Option<string?>("--display-name", "Display name for the node");
var nodesUpdateOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
nodesUpdateCmd.AddArgument(nodesUpdateNodeIdArg);
nodesUpdateCmd.AddOption(nodesUpdateDisplayNameOpt);
nodesUpdateCmd.AddOption(nodesUpdateOrgOpt);
nodesUpdateCmd.SetHandler(async (string nodeId, string? displayName, string? orgId) =>
{
    await NodesUpdateNodeCommand.ExecuteAsync(nodeId, displayName, orgId, CancellationToken.None);
}, nodesUpdateNodeIdArg, nodesUpdateDisplayNameOpt, nodesUpdateOrgOpt);

var nodesDecommissionCmd = new Command("decommission", "Decommission a node (permanent)");
var nodesDecommissionNodeIdArg = new Argument<string>("node-id", "Node ID");
var nodesDecommissionOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
var nodesDecommissionForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
nodesDecommissionCmd.AddArgument(nodesDecommissionNodeIdArg);
nodesDecommissionCmd.AddOption(nodesDecommissionOrgOpt);
nodesDecommissionCmd.AddOption(nodesDecommissionForceOpt);
nodesDecommissionCmd.SetHandler(async (string nodeId, string? orgId, bool force) =>
{
    await NodesDecommissionNodeCommand.ExecuteAsync(nodeId, orgId, force, CancellationToken.None);
}, nodesDecommissionNodeIdArg, nodesDecommissionOrgOpt, nodesDecommissionForceOpt);

var nodesMaintenanceCmd = new Command("maintenance", "Node maintenance mode commands");

var nodesMaintenanceStartCmd = new Command("start", "Put node into maintenance mode");
var nodesMaintenanceStartNodeIdArg = new Argument<string>("node-id", "Node ID");
var nodesMaintenanceStartOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
nodesMaintenanceStartCmd.AddArgument(nodesMaintenanceStartNodeIdArg);
nodesMaintenanceStartCmd.AddOption(nodesMaintenanceStartOrgOpt);
nodesMaintenanceStartCmd.SetHandler(async (string nodeId, string? orgId) =>
{
    await NodesEnterMaintenanceCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
}, nodesMaintenanceStartNodeIdArg, nodesMaintenanceStartOrgOpt);

var nodesMaintenanceStopCmd = new Command("stop", "Take node out of maintenance mode");
var nodesMaintenanceStopNodeIdArg = new Argument<string>("node-id", "Node ID");
var nodesMaintenanceStopOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
nodesMaintenanceStopCmd.AddArgument(nodesMaintenanceStopNodeIdArg);
nodesMaintenanceStopCmd.AddOption(nodesMaintenanceStopOrgOpt);
nodesMaintenanceStopCmd.SetHandler(async (string nodeId, string? orgId) =>
{
    await NodesExitMaintenanceCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
}, nodesMaintenanceStopNodeIdArg, nodesMaintenanceStopOrgOpt);

nodesMaintenanceCmd.AddCommand(nodesMaintenanceStartCmd);
nodesMaintenanceCmd.AddCommand(nodesMaintenanceStopCmd);

nodesCmd.AddCommand(nodesListCmd);
nodesCmd.AddCommand(nodesGetCmd);
nodesCmd.AddCommand(nodesUpdateCmd);
nodesCmd.AddCommand(nodesDecommissionCmd);
nodesCmd.AddCommand(nodesMaintenanceCmd);

// ============================================================================
// ENROLLMENT COMMANDS
// ============================================================================

var enrollmentCmd = new Command("enrollment", "Agent enrollment token management");

var enrollmentTokensCmd = new Command("tokens", "Enrollment token management");

var enrollmentTokensCreateCmd = new Command("create", "Create a new enrollment token");
var enrollmentTokensCreateLabelOpt = new Option<string?>("--label", "Label for the token");
var enrollmentTokensCreateExpiresOpt = new Option<int?>("--expires", "Token expiration in minutes (default: 60)");
var enrollmentTokensCreateOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
enrollmentTokensCreateCmd.AddOption(enrollmentTokensCreateLabelOpt);
enrollmentTokensCreateCmd.AddOption(enrollmentTokensCreateExpiresOpt);
enrollmentTokensCreateCmd.AddOption(enrollmentTokensCreateOrgOpt);
enrollmentTokensCreateCmd.SetHandler(async (string? label, int? expires, string? orgId) =>
{
    await NodesCreateTokenCommand.ExecuteAsync(label, expires, orgId, CancellationToken.None);
}, enrollmentTokensCreateLabelOpt, enrollmentTokensCreateExpiresOpt, enrollmentTokensCreateOrgOpt);

var enrollmentTokensListCmd = new Command("list", "List active enrollment tokens");
var enrollmentTokensListOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
enrollmentTokensListCmd.AddOption(enrollmentTokensListOrgOpt);
enrollmentTokensListCmd.SetHandler(async (string? orgId) =>
{
    await NodesListTokensCommand.ExecuteAsync(orgId, CancellationToken.None);
}, enrollmentTokensListOrgOpt);

var enrollmentTokensRevokeCmd = new Command("revoke", "Revoke an enrollment token");
var enrollmentTokensRevokeIdArg = new Argument<string>("token-id", "Token ID to revoke");
var enrollmentTokensRevokeOrgOpt = new Option<string?>("--org", "Organization ID (defaults to current org)");
var enrollmentTokensRevokeForceOpt = new Option<bool>("--force", "Skip confirmation prompt");
enrollmentTokensRevokeCmd.AddArgument(enrollmentTokensRevokeIdArg);
enrollmentTokensRevokeCmd.AddOption(enrollmentTokensRevokeOrgOpt);
enrollmentTokensRevokeCmd.AddOption(enrollmentTokensRevokeForceOpt);
enrollmentTokensRevokeCmd.SetHandler(async (string tokenId, string? orgId, bool force) =>
{
    await NodesRevokeTokenCommand.ExecuteAsync(tokenId, orgId, force, CancellationToken.None);
}, enrollmentTokensRevokeIdArg, enrollmentTokensRevokeOrgOpt, enrollmentTokensRevokeForceOpt);

enrollmentTokensCmd.AddCommand(enrollmentTokensCreateCmd);
enrollmentTokensCmd.AddCommand(enrollmentTokensListCmd);
enrollmentTokensCmd.AddCommand(enrollmentTokensRevokeCmd);

enrollmentCmd.AddCommand(enrollmentTokensCmd);

// ============================================================================
// NOTIFICATIONS COMMANDS
// ============================================================================

var notificationsCmd = new Command("notifications", "Notification service commands");

var notificationsLogsCmd = new Command("logs", "List notification logs");
var notificationsLimitOpt = new Option<int?>("--limit", "Maximum number of logs to return (default 50, max 100)");
var notificationsStatusOpt = new Option<string?>("--status", "Filter by status (sent, failed, pending)");
var notificationsOrgOpt = new Option<Guid?>("--org", "Filter by organization ID");
notificationsLogsCmd.AddOption(notificationsLimitOpt);
notificationsLogsCmd.AddOption(notificationsStatusOpt);
notificationsLogsCmd.AddOption(notificationsOrgOpt);
notificationsLogsCmd.SetHandler(async (InvocationContext ctx) =>
{
    var limit = ctx.ParseResult.GetValueForOption(notificationsLimitOpt);
    var status = ctx.ParseResult.GetValueForOption(notificationsStatusOpt);
    var orgId = ctx.ParseResult.GetValueForOption(notificationsOrgOpt);
    ctx.ExitCode = await ListLogsCommand.ExecuteAsync(limit, status, orgId, ctx.GetCancellationToken());
});

notificationsCmd.AddCommand(notificationsLogsCmd);

var notificationsTestCmd = new Command("test", "Send a test notification to Discord");
var notificationsTestTitleOpt = new Option<string?>("--title", "Notification title");
var notificationsTestMessageOpt = new Option<string?>("--message", "Notification message");
var notificationsTestSeverityOpt = new Option<string?>("--severity", "Severity level (Info, Warning, Error, Critical)");
var notificationsTestOrgOpt = new Option<Guid?>("--org", "Organization ID");
notificationsTestCmd.AddOption(notificationsTestTitleOpt);
notificationsTestCmd.AddOption(notificationsTestMessageOpt);
notificationsTestCmd.AddOption(notificationsTestSeverityOpt);
notificationsTestCmd.AddOption(notificationsTestOrgOpt);
notificationsTestCmd.SetHandler(async (InvocationContext ctx) =>
{
    var title = ctx.ParseResult.GetValueForOption(notificationsTestTitleOpt);
    var message = ctx.ParseResult.GetValueForOption(notificationsTestMessageOpt);
    var severity = ctx.ParseResult.GetValueForOption(notificationsTestSeverityOpt);
    var orgId = ctx.ParseResult.GetValueForOption(notificationsTestOrgOpt);
    ctx.ExitCode = await SendTestCommand.ExecuteAsync(title, message, severity, orgId, ctx.GetCancellationToken());
});
notificationsCmd.AddCommand(notificationsTestCmd);

// ============================================================================
// DISCORD COMMANDS
// ============================================================================

var discordCmd = new Command("discord", "Discord service commands");

var discordStatusCmd = new Command("status", "Check Discord bot status and platform health");
discordStatusCmd.SetHandler(async (InvocationContext ctx) =>
{
    ctx.ExitCode = await DiscordStatusCommand.ExecuteAsync(ctx.GetCancellationToken());
});

discordCmd.AddCommand(discordStatusCmd);

var discordChannelsCmd = new Command("channels", "List Discord channels the bot can see");
var discordGuildIdOpt = new Option<ulong?>("--guild", "Filter by specific guild ID");
discordChannelsCmd.AddOption(discordGuildIdOpt);
discordChannelsCmd.SetHandler(async (InvocationContext ctx) =>
{
    var guildId = ctx.ParseResult.GetValueForOption(discordGuildIdOpt);
    ctx.ExitCode = await ChannelsCommand.ExecuteAsync(guildId, ctx.GetCancellationToken());
});
discordCmd.AddCommand(discordChannelsCmd);

// ============================================================================
// COMMANDS LIST
// ============================================================================

var commandsCmd = new Command("commands", "List available commands and usage");
commandsCmd.SetHandler(async () =>
{
    await CommandsCommand.ExecuteAsync(root);
});

// ============================================================================
// VERSION COMMAND
// ============================================================================

var versionCmd = new Command("version", "Show CLI build and breaking change info");
versionCmd.SetHandler(async () =>
{
    await VersionCommand.ExecuteAsync();
});

// ============================================================================
// LEGACY PING COMMAND (keeping for backwards compatibility)
// ============================================================================

var urlOpt = new Option<string>(
    name: "--url",
    getDefaultValue: () => "http://localhost:5000/healthz",
    description: "Health endpoint URL");

var ping = new Command("ping", "Ping a service health endpoint (legacy command, prefer 'gateway health')");
ping.AddOption(urlOpt);

ping.SetHandler(async (string url) =>
{
    using var http = new HttpClient();
    var res = await http.GetAsync(url);
    var body = await res.Content.ReadAsStringAsync();

    AnsiConsole.MarkupLine($"[bold]{(int)res.StatusCode}[/] {res.ReasonPhrase}");
    if (!string.IsNullOrWhiteSpace(body))
        AnsiConsole.MarkupLine($"[dim]{body}[/]");
}, urlOpt);

// ============================================================================
// REGISTER ALL COMMANDS
// ============================================================================

root.AddCommand(authCmd);
root.AddCommand(meCmd);
root.AddCommand(identityCmd);
root.AddCommand(memberCmd);
root.AddCommand(secretCmd);
root.AddCommand(keyvaultCmd);
root.AddCommand(gatewayCmd);
root.AddCommand(nodesCmd);
root.AddCommand(enrollmentCmd);
root.AddCommand(notificationsCmd);
root.AddCommand(discordCmd);
root.AddCommand(commandsCmd);
root.AddCommand(versionCmd);
root.AddCommand(ping);

return await root.InvokeAsync(args);
