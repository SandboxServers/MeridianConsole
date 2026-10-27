using System.CommandLine;
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

root.SetAction(_ =>
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
var clientIdOpt = new Option<string?>("--client-id") { Description = "Client ID (defaults to dev-client)" };
var clientSecretOpt = new Option<string?>("--client-secret") { Description = "Client secret (defaults to dev-secret)" };
var identityUrlOpt = new Option<Uri?>("--identity-url") { Description = "Identity service URL" };
authLoginCmd.Options.Add(clientIdOpt);
authLoginCmd.Options.Add(clientSecretOpt);
authLoginCmd.Options.Add(identityUrlOpt);
authLoginCmd.SetAction(async (parseResult, _) =>
{
    var clientId = parseResult.GetValue(clientIdOpt);
    var clientSecret = parseResult.GetValue(clientSecretOpt);
    var identityUrl = parseResult.GetValue(identityUrlOpt);
    await LoginCommand.ExecuteAsync(clientId, clientSecret, identityUrl, CancellationToken.None);
});

var authStatusCmd = new Command("status", "Show authentication status and configuration");
authStatusCmd.SetAction(async (_, _) =>
{
    await AuthStatusCommand.ExecuteAsync(CancellationToken.None);
});

var authLogoutCmd = new Command("logout", "Clear authentication tokens and log out");
authLogoutCmd.SetAction(async (_, _) =>
{
    await LogoutCommand.ExecuteAsync(CancellationToken.None);
});

authCmd.Subcommands.Add(authLoginCmd);
authCmd.Subcommands.Add(authStatusCmd);
authCmd.Subcommands.Add(authLogoutCmd);

// ============================================================================
// IDENTITY COMMANDS
// ============================================================================

var identityCmd = new Command("identity", "Identity service commands");
var identityOrgsCmd = new Command("orgs", "Organization management");

var identityOrgsListCmd = new Command("list", "List organizations");
identityOrgsListCmd.SetAction(async (_, _) =>
{
    await IdentityListOrgsCommand.ExecuteAsync(CancellationToken.None);
});

var identityOrgsGetCmd = new Command("get", "Get an organization by id");
var identityOrgIdArg = new Argument<string>("org-id") { Description = "Organization ID" };
identityOrgsGetCmd.Arguments.Add(identityOrgIdArg);
identityOrgsGetCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identityOrgIdArg)!;
    await IdentityGetOrgCommand.ExecuteAsync(orgId, CancellationToken.None);
});

var identityOrgsCreateCmd = new Command("create", "Create an organization");
var identityOrgNameOpt = new Option<string>("--name") { Description = "Organization name", Required = true };
var identityOrgDescriptionOpt = new Option<string?>("--description") { Description = "Organization description" };
identityOrgsCreateCmd.Options.Add(identityOrgNameOpt);
identityOrgsCreateCmd.Options.Add(identityOrgDescriptionOpt);
identityOrgsCreateCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(identityOrgNameOpt)!;
    var description = parseResult.GetValue(identityOrgDescriptionOpt);
    await IdentityCreateOrgCommand.ExecuteAsync(name, description, CancellationToken.None);
});

var identityOrgsUpdateCmd = new Command("update", "Update an organization");
var identityUpdateOrgIdArg = new Argument<string>("org-id") { Description = "Organization ID" };
var identityUpdateNameOpt = new Option<string?>("--name") { Description = "Organization name" };
var identityUpdateDescriptionOpt = new Option<string?>("--description") { Description = "Organization description" };
identityOrgsUpdateCmd.Arguments.Add(identityUpdateOrgIdArg);
identityOrgsUpdateCmd.Options.Add(identityUpdateNameOpt);
identityOrgsUpdateCmd.Options.Add(identityUpdateDescriptionOpt);
identityOrgsUpdateCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identityUpdateOrgIdArg)!;
    var name = parseResult.GetValue(identityUpdateNameOpt);
    var description = parseResult.GetValue(identityUpdateDescriptionOpt);
    await IdentityUpdateOrgCommand.ExecuteAsync(orgId, name, description, CancellationToken.None);
});

var identityOrgsDeleteCmd = new Command("delete", "Delete an organization");
var identityDeleteOrgIdArg = new Argument<string>("org-id") { Description = "Organization ID" };
var identityDeleteForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
identityOrgsDeleteCmd.Arguments.Add(identityDeleteOrgIdArg);
identityOrgsDeleteCmd.Options.Add(identityDeleteForceOpt);
identityOrgsDeleteCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identityDeleteOrgIdArg)!;
    var force = parseResult.GetValue(identityDeleteForceOpt);
    await IdentityDeleteOrgCommand.ExecuteAsync(orgId, force, CancellationToken.None);
});

var identityOrgsSwitchCmd = new Command("switch", "Switch to an organization");
var identitySwitchOrgIdArg = new Argument<string>("org-id") { Description = "Organization ID" };
identityOrgsSwitchCmd.Arguments.Add(identitySwitchOrgIdArg);
identityOrgsSwitchCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identitySwitchOrgIdArg)!;
    await IdentitySwitchOrgCommand.ExecuteAsync(orgId, CancellationToken.None);
});

var identityOrgsSearchCmd = new Command("search", "Search organizations");
var identityOrgsQueryOpt = new Option<string>("--query") { Description = "Search query", Required = true };
identityOrgsSearchCmd.Options.Add(identityOrgsQueryOpt);
identityOrgsSearchCmd.SetAction(async (parseResult, _) =>
{
    var query = parseResult.GetValue(identityOrgsQueryOpt)!;
    await IdentitySearchOrgsCommand.ExecuteAsync(query, CancellationToken.None);
});

identityOrgsCmd.Subcommands.Add(identityOrgsListCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsGetCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsCreateCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsUpdateCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsDeleteCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsSwitchCmd);
identityOrgsCmd.Subcommands.Add(identityOrgsSearchCmd);

identityCmd.Subcommands.Add(identityOrgsCmd);

// ========================================================================
// IDENTITY USERS COMMANDS
// ========================================================================

var identityUsersCmd = new Command("users", "User management");

var identityUsersListCmd = new Command("list", "List users");
var identityUsersListOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityUsersListCmd.Options.Add(identityUsersListOrgOpt);
identityUsersListCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identityUsersListOrgOpt);
    await IdentityListUsersCommand.ExecuteAsync(orgId, CancellationToken.None);
});

var identityUsersGetCmd = new Command("get", "Get a user by id");
var identityUserIdArg = new Argument<string>("user-id") { Description = "User ID" };
var identityUsersGetOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityUsersGetCmd.Arguments.Add(identityUserIdArg);
identityUsersGetCmd.Options.Add(identityUsersGetOrgOpt);
identityUsersGetCmd.SetAction(async (parseResult, _) =>
{
    var userId = parseResult.GetValue(identityUserIdArg)!;
    var orgId = parseResult.GetValue(identityUsersGetOrgOpt);
    await IdentityGetUserCommand.ExecuteAsync(userId, orgId, CancellationToken.None);
});

var identityUsersCreateCmd = new Command("create", "Create a user");
var identityUserEmailOpt = new Option<string>("--email") { Description = "User email", Required = true };
var identityUserOrgOpt = new Option<string>("--org") { Description = "Organization ID", Required = true };
var identityUserNameOpt = new Option<string?>("--name") { Description = "Display name" };
identityUsersCreateCmd.Options.Add(identityUserEmailOpt);
identityUsersCreateCmd.Options.Add(identityUserOrgOpt);
identityUsersCreateCmd.Options.Add(identityUserNameOpt);
identityUsersCreateCmd.SetAction(async (parseResult, _) =>
{
    var email = parseResult.GetValue(identityUserEmailOpt)!;
    var orgId = parseResult.GetValue(identityUserOrgOpt)!;
    var name = parseResult.GetValue(identityUserNameOpt);
    await IdentityCreateUserCommand.ExecuteAsync(email, orgId, name, CancellationToken.None);
});

var identityUsersUpdateCmd = new Command("update", "Update a user");
var identityUsersUpdateIdArg = new Argument<string>("user-id") { Description = "User ID" };
var identityUsersUpdateEmailOpt = new Option<string?>("--email") { Description = "User email" };
var identityUsersUpdateNameOpt = new Option<string?>("--name") { Description = "Display name" };
var identityUsersUpdateOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityUsersUpdateCmd.Arguments.Add(identityUsersUpdateIdArg);
identityUsersUpdateCmd.Options.Add(identityUsersUpdateEmailOpt);
identityUsersUpdateCmd.Options.Add(identityUsersUpdateNameOpt);
identityUsersUpdateCmd.Options.Add(identityUsersUpdateOrgOpt);
identityUsersUpdateCmd.SetAction(async (parseResult, _) =>
{
    var userId = parseResult.GetValue(identityUsersUpdateIdArg)!;
    var email = parseResult.GetValue(identityUsersUpdateEmailOpt);
    var name = parseResult.GetValue(identityUsersUpdateNameOpt);
    var orgId = parseResult.GetValue(identityUsersUpdateOrgOpt);
    await IdentityUpdateUserCommand.ExecuteAsync(userId, email, name, orgId, CancellationToken.None);
});

var identityUsersDeleteCmd = new Command("delete", "Delete a user");
var identityUsersDeleteIdArg = new Argument<string>("user-id") { Description = "User ID" };
var identityUsersDeleteForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
var identityUsersDeleteOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityUsersDeleteCmd.Arguments.Add(identityUsersDeleteIdArg);
identityUsersDeleteCmd.Options.Add(identityUsersDeleteForceOpt);
identityUsersDeleteCmd.Options.Add(identityUsersDeleteOrgOpt);
identityUsersDeleteCmd.SetAction(async (parseResult, _) =>
{
    var userId = parseResult.GetValue(identityUsersDeleteIdArg)!;
    var force = parseResult.GetValue(identityUsersDeleteForceOpt);
    var orgId = parseResult.GetValue(identityUsersDeleteOrgOpt);
    await IdentityDeleteUserCommand.ExecuteAsync(userId, orgId, force, CancellationToken.None);
});

var identityUsersSearchCmd = new Command("search", "Search users");
var identityUsersSearchQueryOpt = new Option<string>("--query") { Description = "Search query", Required = true };
var identityUsersSearchOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityUsersSearchCmd.Options.Add(identityUsersSearchQueryOpt);
identityUsersSearchCmd.Options.Add(identityUsersSearchOrgOpt);
identityUsersSearchCmd.SetAction(async (parseResult, _) =>
{
    var query = parseResult.GetValue(identityUsersSearchQueryOpt)!;
    var orgId = parseResult.GetValue(identityUsersSearchOrgOpt);
    await IdentitySearchUsersCommand.ExecuteAsync(query, orgId, CancellationToken.None);
});

identityUsersCmd.Subcommands.Add(identityUsersListCmd);
identityUsersCmd.Subcommands.Add(identityUsersGetCmd);
identityUsersCmd.Subcommands.Add(identityUsersCreateCmd);
identityUsersCmd.Subcommands.Add(identityUsersUpdateCmd);
identityUsersCmd.Subcommands.Add(identityUsersDeleteCmd);
identityUsersCmd.Subcommands.Add(identityUsersSearchCmd);

identityCmd.Subcommands.Add(identityUsersCmd);

// ========================================================================
// IDENTITY ROLES COMMANDS
// ========================================================================

var identityRolesCmd = new Command("roles", "Role management");

var identityRolesListCmd = new Command("list", "List roles");
var identityRolesListOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesListCmd.Options.Add(identityRolesListOrgOpt);
identityRolesListCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(identityRolesListOrgOpt);
    await IdentityListRolesCommand.ExecuteAsync(orgId, CancellationToken.None);
});

var identityRolesGetCmd = new Command("get", "Get a role by id");
var identityRoleIdArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesGetOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesGetCmd.Arguments.Add(identityRoleIdArg);
identityRolesGetCmd.Options.Add(identityRolesGetOrgOpt);
identityRolesGetCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRoleIdArg)!;
    var orgId = parseResult.GetValue(identityRolesGetOrgOpt);
    await IdentityGetRoleCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
});

var identityRolesCreateCmd = new Command("create", "Create a role");
var identityRoleNameOpt = new Option<string>("--name") { Description = "Role name", Required = true };
var identityRoleOrgOpt = new Option<string>("--org") { Description = "Organization ID", Required = true };
var identityRoleDescriptionOpt = new Option<string?>("--description") { Description = "Role description" };
var identityRolePermissionsOpt = new Option<string?>("--permissions") { Description = "Comma-separated permissions" };
identityRolesCreateCmd.Options.Add(identityRoleNameOpt);
identityRolesCreateCmd.Options.Add(identityRoleOrgOpt);
identityRolesCreateCmd.Options.Add(identityRoleDescriptionOpt);
identityRolesCreateCmd.Options.Add(identityRolePermissionsOpt);
identityRolesCreateCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(identityRoleNameOpt)!;
    var orgId = parseResult.GetValue(identityRoleOrgOpt)!;
    var description = parseResult.GetValue(identityRoleDescriptionOpt);
    var permissions = parseResult.GetValue(identityRolePermissionsOpt);
    await IdentityCreateRoleCommand.ExecuteAsync(name, orgId, description, permissions, CancellationToken.None);
});

var identityRolesUpdateCmd = new Command("update", "Update a role");
var identityRolesUpdateIdArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesUpdateNameOpt = new Option<string?>("--name") { Description = "Role name" };
var identityRolesUpdateDescriptionOpt = new Option<string?>("--description") { Description = "Role description" };
var identityRolesUpdatePermissionsOpt = new Option<string?>("--permissions") { Description = "Comma-separated permissions" };
var identityRolesUpdateOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesUpdateCmd.Arguments.Add(identityRolesUpdateIdArg);
identityRolesUpdateCmd.Options.Add(identityRolesUpdateNameOpt);
identityRolesUpdateCmd.Options.Add(identityRolesUpdateDescriptionOpt);
identityRolesUpdateCmd.Options.Add(identityRolesUpdatePermissionsOpt);
identityRolesUpdateCmd.Options.Add(identityRolesUpdateOrgOpt);
identityRolesUpdateCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRolesUpdateIdArg)!;
    var name = parseResult.GetValue(identityRolesUpdateNameOpt);
    var description = parseResult.GetValue(identityRolesUpdateDescriptionOpt);
    var permissions = parseResult.GetValue(identityRolesUpdatePermissionsOpt);
    var orgId = parseResult.GetValue(identityRolesUpdateOrgOpt);
    await IdentityUpdateRoleCommand.ExecuteAsync(roleId, orgId, name, description, permissions, CancellationToken.None);
});

var identityRolesDeleteCmd = new Command("delete", "Delete a role");
var identityRolesDeleteIdArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesDeleteOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesDeleteCmd.Arguments.Add(identityRolesDeleteIdArg);
identityRolesDeleteCmd.Options.Add(identityRolesDeleteOrgOpt);
identityRolesDeleteCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRolesDeleteIdArg)!;
    var orgId = parseResult.GetValue(identityRolesDeleteOrgOpt);
    await IdentityDeleteRoleCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
});

var identityRolesMembersCmd = new Command("members", "List members with a role");
var identityRolesMembersIdArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesMembersOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesMembersCmd.Arguments.Add(identityRolesMembersIdArg);
identityRolesMembersCmd.Options.Add(identityRolesMembersOrgOpt);
identityRolesMembersCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRolesMembersIdArg)!;
    var orgId = parseResult.GetValue(identityRolesMembersOrgOpt);
    await IdentityListRoleMembersCommand.ExecuteAsync(roleId, orgId, CancellationToken.None);
});

var identityRolesAssignCmd = new Command("assign", "Assign a role to a user");
var identityRolesAssignRoleArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesAssignUserOpt = new Option<string>("--user") { Description = "User ID", Required = true };
var identityRolesAssignOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesAssignCmd.Arguments.Add(identityRolesAssignRoleArg);
identityRolesAssignCmd.Options.Add(identityRolesAssignUserOpt);
identityRolesAssignCmd.Options.Add(identityRolesAssignOrgOpt);
identityRolesAssignCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRolesAssignRoleArg)!;
    var userId = parseResult.GetValue(identityRolesAssignUserOpt)!;
    var orgId = parseResult.GetValue(identityRolesAssignOrgOpt);
    await IdentityAssignRoleCommand.ExecuteAsync(roleId, userId, orgId, CancellationToken.None);
});

var identityRolesRevokeCmd = new Command("revoke", "Revoke a role from a user");
var identityRolesRevokeRoleArg = new Argument<string>("role-id") { Description = "Role ID" };
var identityRolesRevokeUserOpt = new Option<string>("--user") { Description = "User ID", Required = true };
var identityRolesRevokeOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesRevokeCmd.Arguments.Add(identityRolesRevokeRoleArg);
identityRolesRevokeCmd.Options.Add(identityRolesRevokeUserOpt);
identityRolesRevokeCmd.Options.Add(identityRolesRevokeOrgOpt);
identityRolesRevokeCmd.SetAction(async (parseResult, _) =>
{
    var roleId = parseResult.GetValue(identityRolesRevokeRoleArg)!;
    var userId = parseResult.GetValue(identityRolesRevokeUserOpt)!;
    var orgId = parseResult.GetValue(identityRolesRevokeOrgOpt);
    await IdentityRevokeRoleCommand.ExecuteAsync(roleId, userId, orgId, CancellationToken.None);
});

var identityRolesSearchCmd = new Command("search", "Search roles");
var identityRolesSearchQueryOpt = new Option<string>("--query") { Description = "Search query", Required = true };
var identityRolesSearchOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityRolesSearchCmd.Options.Add(identityRolesSearchQueryOpt);
identityRolesSearchCmd.Options.Add(identityRolesSearchOrgOpt);
identityRolesSearchCmd.SetAction(async (parseResult, _) =>
{
    var query = parseResult.GetValue(identityRolesSearchQueryOpt)!;
    var orgId = parseResult.GetValue(identityRolesSearchOrgOpt);
    await IdentitySearchRolesCommand.ExecuteAsync(query, orgId, CancellationToken.None);
});

identityRolesCmd.Subcommands.Add(identityRolesListCmd);
identityRolesCmd.Subcommands.Add(identityRolesGetCmd);
identityRolesCmd.Subcommands.Add(identityRolesCreateCmd);
identityRolesCmd.Subcommands.Add(identityRolesUpdateCmd);
identityRolesCmd.Subcommands.Add(identityRolesDeleteCmd);
identityRolesCmd.Subcommands.Add(identityRolesMembersCmd);
identityRolesCmd.Subcommands.Add(identityRolesAssignCmd);
identityRolesCmd.Subcommands.Add(identityRolesRevokeCmd);
identityRolesCmd.Subcommands.Add(identityRolesSearchCmd);

identityCmd.Subcommands.Add(identityRolesCmd);

// ============================================================================
// IDENTITY MEMBERS COMMANDS (Claim management)
// ============================================================================

var identityMembersCmd = new Command("members", "Member claim management");

var identityMembersGrantCmd = new Command("grant", "Grant a permission to a member");
var grantMemberIdArg = new Argument<string>("member-id") { Description = "Member ID (user ID)" };
var grantPermissionArg = new Argument<string>("permission") { Description = "Permission to grant (e.g., secrets:read:oauth)" };
var grantOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
var grantExpiresOpt = new Option<string?>("--expires") { Description = "Expiration (e.g., 1h, 7d, 30d, or ISO 8601 date)" };
identityMembersGrantCmd.Arguments.Add(grantMemberIdArg);
identityMembersGrantCmd.Arguments.Add(grantPermissionArg);
identityMembersGrantCmd.Options.Add(grantOrgOpt);
identityMembersGrantCmd.Options.Add(grantExpiresOpt);
identityMembersGrantCmd.SetAction(async (parseResult, _) =>
{
    var memberId = parseResult.GetValue(grantMemberIdArg)!;
    var permission = parseResult.GetValue(grantPermissionArg)!;
    var orgId = parseResult.GetValue(grantOrgOpt);
    var expires = parseResult.GetValue(grantExpiresOpt);
    await GrantClaimCommand.ExecuteAsync(memberId, permission, orgId, expires, CancellationToken.None);
});

var identityMembersRevokeCmd = new Command("revoke", "Revoke a claim from a member");
var revokeMemberIdArg = new Argument<string>("member-id") { Description = "Member ID (user ID)" };
var revokeClaimIdArg = new Argument<string>("claim-id") { Description = "Claim ID to revoke" };
var revokeOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
var revokeForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
identityMembersRevokeCmd.Arguments.Add(revokeMemberIdArg);
identityMembersRevokeCmd.Arguments.Add(revokeClaimIdArg);
identityMembersRevokeCmd.Options.Add(revokeOrgOpt);
identityMembersRevokeCmd.Options.Add(revokeForceOpt);
identityMembersRevokeCmd.SetAction(async (parseResult, _) =>
{
    var memberId = parseResult.GetValue(revokeMemberIdArg)!;
    var claimId = parseResult.GetValue(revokeClaimIdArg)!;
    var orgId = parseResult.GetValue(revokeOrgOpt);
    var force = parseResult.GetValue(revokeForceOpt);
    await RevokeClaimCommand.ExecuteAsync(memberId, claimId, orgId, force, CancellationToken.None);
});

var identityMembersClaimsCmd = new Command("claims", "List custom claims for a member");
var claimsMemberIdArg = new Argument<string>("member-id") { Description = "Member ID (user ID)" };
var claimsOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
identityMembersClaimsCmd.Arguments.Add(claimsMemberIdArg);
identityMembersClaimsCmd.Options.Add(claimsOrgOpt);
identityMembersClaimsCmd.SetAction(async (parseResult, _) =>
{
    var memberId = parseResult.GetValue(claimsMemberIdArg)!;
    var orgId = parseResult.GetValue(claimsOrgOpt);
    await ListClaimsCommand.ExecuteAsync(memberId, orgId, CancellationToken.None);
});

identityMembersCmd.Subcommands.Add(identityMembersGrantCmd);
identityMembersCmd.Subcommands.Add(identityMembersRevokeCmd);
identityMembersCmd.Subcommands.Add(identityMembersClaimsCmd);

identityCmd.Subcommands.Add(identityMembersCmd);

// ============================================================================
// ME COMMANDS (Self-service)
// ============================================================================

var meCmd = new Command("me", "Self-service commands for current user");

var meProfileCmd = new Command("profile", "Get your profile information");
meProfileCmd.SetAction(async (_, _) =>
{
    await GetProfileCommand.ExecuteAsync(CancellationToken.None);
});

var meUpdateProfileCmd = new Command("update", "Update your profile");
var meDisplayNameOpt = new Option<string?>("--name") { Description = "Display name" };
var mePreferredOrgOpt = new Option<string?>("--preferred-org") { Description = "Preferred organization ID" };
meUpdateProfileCmd.Options.Add(meDisplayNameOpt);
meUpdateProfileCmd.Options.Add(mePreferredOrgOpt);
meUpdateProfileCmd.SetAction(async (parseResult, _) =>
{
    var displayName = parseResult.GetValue(meDisplayNameOpt);
    var preferredOrgId = parseResult.GetValue(mePreferredOrgOpt);
    await UpdateProfileCommand.ExecuteAsync(displayName, preferredOrgId, CancellationToken.None);
});

var meOrgsCmd = new Command("orgs", "List your organizations");
meOrgsCmd.SetAction(async (_, _) =>
{
    await ListOrganizationsCommand.ExecuteAsync(CancellationToken.None);
});

var meLinkedAccountsCmd = new Command("linked-accounts", "List your linked accounts");
meLinkedAccountsCmd.SetAction(async (_, _) =>
{
    await ListLinkedAccountsCommand.ExecuteAsync(CancellationToken.None);
});

var mePermissionsCmd = new Command("permissions", "List your permissions");
mePermissionsCmd.SetAction(async (_, _) =>
{
    await ListPermissionsCommand.ExecuteAsync(CancellationToken.None);
});

var meSessionsCmd = new Command("sessions", "Session management");

var meSessionsListCmd = new Command("list", "List your active sessions");
meSessionsListCmd.SetAction(async (_, _) =>
{
    await ListSessionsCommand.ExecuteAsync(CancellationToken.None);
});

var meSessionsRevokeCmd = new Command("revoke", "Revoke a session");
var meSessionIdArg = new Argument<string>("session-id") { Description = "Session ID to revoke" };
meSessionsRevokeCmd.Arguments.Add(meSessionIdArg);
meSessionsRevokeCmd.SetAction(async (parseResult, _) =>
{
    var sessionId = parseResult.GetValue(meSessionIdArg)!;
    await RevokeSessionCommand.ExecuteAsync(sessionId, CancellationToken.None);
});

var meSessionsRevokeAllCmd = new Command("revoke-all", "Revoke all sessions (logout everywhere)");
meSessionsRevokeAllCmd.SetAction(async (_, _) =>
{
    await RevokeAllSessionsCommand.ExecuteAsync(CancellationToken.None);
});

meSessionsCmd.Subcommands.Add(meSessionsListCmd);
meSessionsCmd.Subcommands.Add(meSessionsRevokeCmd);
meSessionsCmd.Subcommands.Add(meSessionsRevokeAllCmd);

meCmd.Subcommands.Add(meProfileCmd);
meCmd.Subcommands.Add(meUpdateProfileCmd);
meCmd.Subcommands.Add(meOrgsCmd);
meCmd.Subcommands.Add(meLinkedAccountsCmd);
meCmd.Subcommands.Add(mePermissionsCmd);
meCmd.Subcommands.Add(meSessionsCmd);

// ============================================================================
// MEMBER COMMANDS
// ============================================================================

var memberCmd = new Command("member", "Organization member management");

var memberListCmd = new Command("list", "List members of an organization");
var memberOrgIdArg = new Argument<string?>("org-id") { Description = "Organization ID (uses current org if not specified)", Arity = ArgumentArity.ZeroOrOne };
memberListCmd.Arguments.Add(memberOrgIdArg);
memberListCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(memberOrgIdArg);
    await ListMembersCommand.ExecuteAsync(orgId, CancellationToken.None);
});

memberCmd.Subcommands.Add(memberListCmd);

// ============================================================================
// SECRET COMMANDS
// ============================================================================

var secretCmd = new Command("secret", "Secret management");

var secretGetCmd = new Command("get", "Get a single secret by name");
var secretNameArg = new Argument<string>("name") { Description = "Secret name" };
var revealOpt = new Option<bool>("--reveal") { Description = "Reveal the actual secret value (masked by default)" };
var copyOpt = new Option<bool>("--copy") { Description = "Copy secret to clipboard" };
secretGetCmd.Arguments.Add(secretNameArg);
secretGetCmd.Options.Add(revealOpt);
secretGetCmd.Options.Add(copyOpt);
secretGetCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(secretNameArg)!;
    var reveal = parseResult.GetValue(revealOpt);
    var copy = parseResult.GetValue(copyOpt);
    await GetSecretCommand.ExecuteAsync(name, reveal, copy, CancellationToken.None);
});

var secretListCmd = new Command("list", "List secrets by category");
var categoryArg = new Argument<string>("category") { Description = "Secret category (oauth, betterauth, infrastructure)" };
var revealListOpt = new Option<bool>("--reveal") { Description = "Reveal actual secret values (masked by default)" };
secretListCmd.Arguments.Add(categoryArg);
secretListCmd.Options.Add(revealListOpt);
secretListCmd.SetAction(async (parseResult, _) =>
{
    var category = parseResult.GetValue(categoryArg)!;
    var reveal = parseResult.GetValue(revealListOpt);
    await ListSecretsCommand.ExecuteAsync(category, reveal, CancellationToken.None);
});

var secretSetCmd = new Command("set", "Set or update a secret value");
var setSecretNameArg = new Argument<string>("name") { Description = "Secret name" };
var setValueArg = new Argument<string?>("value") { Description = "Secret value (optional, will prompt if not provided)", Arity = ArgumentArity.ZeroOrOne };
var stdinOpt = new Option<bool>("--stdin") { Description = "Read secret value from stdin" };
secretSetCmd.Arguments.Add(setSecretNameArg);
secretSetCmd.Arguments.Add(setValueArg);
secretSetCmd.Options.Add(stdinOpt);
secretSetCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(setSecretNameArg)!;
    var value = parseResult.GetValue(setValueArg);
    var stdin = parseResult.GetValue(stdinOpt);
    await SetSecretCommand.ExecuteAsync(name, value, stdin, CancellationToken.None);
});

var secretRotateCmd = new Command("rotate", "Rotate a secret (generate new value)");
var rotateSecretNameArg = new Argument<string>("name") { Description = "Secret name" };
var forceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
secretRotateCmd.Arguments.Add(rotateSecretNameArg);
secretRotateCmd.Options.Add(forceOpt);
secretRotateCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(rotateSecretNameArg)!;
    var force = parseResult.GetValue(forceOpt);
    await RotateSecretCommand.ExecuteAsync(name, force, CancellationToken.None);
});

var secretDeleteCmd = new Command("delete", "Delete a secret");
var deleteSecretNameArg = new Argument<string>("name") { Description = "Secret name" };
var deleteSecretForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
secretDeleteCmd.Arguments.Add(deleteSecretNameArg);
secretDeleteCmd.Options.Add(deleteSecretForceOpt);
secretDeleteCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(deleteSecretNameArg)!;
    var force = parseResult.GetValue(deleteSecretForceOpt);
    await DeleteSecretCommand.ExecuteAsync(name, force, CancellationToken.None);
});

var certListCmd = new Command("list-certs", "List certificates");
var certVaultNameOpt = new Option<string?>("--vault") { Description = "Key Vault name (optional)" };
certListCmd.Options.Add(certVaultNameOpt);
certListCmd.SetAction(async (parseResult, _) =>
{
    var vaultName = parseResult.GetValue(certVaultNameOpt);
    await ListCertificatesCommand.ExecuteAsync(vaultName, CancellationToken.None);
});

var certImportCmd = new Command("import-cert", "Import a certificate");
var certPathArg = new Argument<string>("path") { Description = "Path to certificate file (.pfx, .p12, .pem, .cer)" };
var certNameOpt = new Option<string?>("--name") { Description = "Certificate name (defaults to filename)" };
var certPasswordOpt = new Option<string?>("--password") { Description = "Certificate password (for PFX/P12)" };
var certVaultOpt = new Option<string?>("--vault") { Description = "Key Vault name (optional)" };
certImportCmd.Arguments.Add(certPathArg);
certImportCmd.Options.Add(certNameOpt);
certImportCmd.Options.Add(certPasswordOpt);
certImportCmd.Options.Add(certVaultOpt);
certImportCmd.SetAction(async (parseResult, _) =>
{
    var path = parseResult.GetValue(certPathArg)!;
    var name = parseResult.GetValue(certNameOpt);
    var password = parseResult.GetValue(certPasswordOpt);
    var vault = parseResult.GetValue(certVaultOpt);
    await ImportCertificateCommand.ExecuteAsync(path, name, password, vault, CancellationToken.None);
});

secretCmd.Subcommands.Add(secretGetCmd);
secretCmd.Subcommands.Add(secretListCmd);
secretCmd.Subcommands.Add(secretSetCmd);
secretCmd.Subcommands.Add(secretRotateCmd);
secretCmd.Subcommands.Add(secretDeleteCmd);
secretCmd.Subcommands.Add(certListCmd);
secretCmd.Subcommands.Add(certImportCmd);

// ============================================================================
// KEY VAULT COMMANDS
// ============================================================================

var keyvaultCmd = new Command("keyvault", "Azure Key Vault management");

var vaultListCmd = new Command("list", "List all Key Vaults");
vaultListCmd.SetAction(async (_, _) =>
{
    await ListVaultsCommand.ExecuteAsync(CancellationToken.None);
});

var vaultCreateCmd = new Command("create", "Create a new Key Vault");
var vaultNameArg = new Argument<string?>("name") { Description = "Vault name (optional, will prompt)", Arity = ArgumentArity.ZeroOrOne };
var vaultLocationOpt = new Option<string?>("--location") { Description = "Azure location" };
vaultCreateCmd.Arguments.Add(vaultNameArg);
vaultCreateCmd.Options.Add(vaultLocationOpt);
vaultCreateCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(vaultNameArg);
    var location = parseResult.GetValue(vaultLocationOpt);
    await CreateVaultCommand.ExecuteAsync(name, location, CancellationToken.None);
});

var vaultGetCmd = new Command("get", "Get Key Vault details");
var vaultGetNameArg = new Argument<string>("name") { Description = "Vault name" };
vaultGetCmd.Arguments.Add(vaultGetNameArg);
vaultGetCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(vaultGetNameArg)!;
    await GetVaultCommand.ExecuteAsync(name, CancellationToken.None);
});

var vaultUpdateCmd = new Command("update", "Update Key Vault properties");
var vaultUpdateNameArg = new Argument<string>("name") { Description = "Vault name" };
var enableSoftDeleteOpt = new Option<bool?>("--enable-soft-delete") { Description = "Enable soft delete" };
var disableSoftDeleteOpt = new Option<bool?>("--disable-soft-delete") { Description = "Disable soft delete" };
var enablePurgeProtectionOpt = new Option<bool?>("--enable-purge-protection") { Description = "Enable purge protection" };
var disablePurgeProtectionOpt = new Option<bool?>("--disable-purge-protection") { Description = "Disable purge protection" };
var retentionDaysOpt = new Option<int?>("--retention-days") { Description = "Soft delete retention days (7-90)" };
var skuOpt = new Option<string?>("--sku") { Description = "Vault SKU (standard or premium)" };

vaultUpdateCmd.Arguments.Add(vaultUpdateNameArg);
vaultUpdateCmd.Options.Add(enableSoftDeleteOpt);
vaultUpdateCmd.Options.Add(disableSoftDeleteOpt);
vaultUpdateCmd.Options.Add(enablePurgeProtectionOpt);
vaultUpdateCmd.Options.Add(disablePurgeProtectionOpt);
vaultUpdateCmd.Options.Add(retentionDaysOpt);
vaultUpdateCmd.Options.Add(skuOpt);

vaultUpdateCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(vaultUpdateNameArg)!;
    var enableSD = parseResult.GetValue(enableSoftDeleteOpt);
    var disableSD = parseResult.GetValue(disableSoftDeleteOpt);
    var enablePP = parseResult.GetValue(enablePurgeProtectionOpt);
    var disablePP = parseResult.GetValue(disablePurgeProtectionOpt);
    var retention = parseResult.GetValue(retentionDaysOpt);
    var sku = parseResult.GetValue(skuOpt);
    // Convert enable/disable flags to nullable bool
    bool? softDelete = enableSD.HasValue ? enableSD : (disableSD.HasValue ? !disableSD : null);
    bool? purgeProtection = enablePP.HasValue ? enablePP : (disablePP.HasValue ? !disablePP : null);

    await UpdateVaultCommand.ExecuteAsync(name, softDelete, purgeProtection, retention, sku, CancellationToken.None);
});

var vaultDeleteCmd = new Command("delete", "Delete a Key Vault");
var vaultDeleteNameArg = new Argument<string>("name") { Description = "Vault name" };
var vaultDeleteForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
vaultDeleteCmd.Arguments.Add(vaultDeleteNameArg);
vaultDeleteCmd.Options.Add(vaultDeleteForceOpt);
vaultDeleteCmd.SetAction(async (parseResult, _) =>
{
    var name = parseResult.GetValue(vaultDeleteNameArg)!;
    var force = parseResult.GetValue(vaultDeleteForceOpt);
    await DeleteVaultCommand.ExecuteAsync(name, force, CancellationToken.None);
});

keyvaultCmd.Subcommands.Add(vaultListCmd);
keyvaultCmd.Subcommands.Add(vaultGetCmd);
keyvaultCmd.Subcommands.Add(vaultCreateCmd);
keyvaultCmd.Subcommands.Add(vaultUpdateCmd);
keyvaultCmd.Subcommands.Add(vaultDeleteCmd);

// ============================================================================
// GATEWAY COMMANDS
// ============================================================================

var gatewayCmd = new Command("gateway", "Gateway and infrastructure diagnostics");

var gatewayHealthCmd = new Command("health", "Check health of all services");
gatewayHealthCmd.SetAction(async (parseResult, cancellationToken) =>
{
    return await HealthCommand.ExecuteAsync(cancellationToken);
});

var gatewayServicesCmd = new Command("services", "List all backend services health (Development only)");
gatewayServicesCmd.SetAction(async (parseResult, cancellationToken) =>
{
    return await ServicesCommand.ExecuteAsync(cancellationToken);
});

var gatewayRoutesCmd = new Command("routes", "List all gateway routes (Development only)");
gatewayRoutesCmd.SetAction(async (parseResult, cancellationToken) =>
{
    return await RoutesCommand.ExecuteAsync(cancellationToken);
});

var gatewayClustersCmd = new Command("clusters", "List all YARP clusters (Development only)");
gatewayClustersCmd.SetAction(async (parseResult, cancellationToken) =>
{
    return await ClustersCommand.ExecuteAsync(cancellationToken);
});

gatewayCmd.Subcommands.Add(gatewayHealthCmd);
gatewayCmd.Subcommands.Add(gatewayServicesCmd);
gatewayCmd.Subcommands.Add(gatewayRoutesCmd);
gatewayCmd.Subcommands.Add(gatewayClustersCmd);

// ============================================================================
// NODES COMMANDS
// ============================================================================

var nodesCmd = new Command("nodes", "Node management commands");

var nodesListCmd = new Command("list", "List nodes");
var nodesListOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
var nodesListStatusOpt = new Option<string?>("--status") { Description = "Filter by status (online, offline, degraded, maintenance)" };
var nodesListSkipOpt = new Option<int?>("--skip") { Description = "Number of items to skip (pagination)" };
nodesListSkipOpt.Validators.Add(result =>
{
    var value = result.GetValueOrDefault<int?>();
    if (value.HasValue && value.Value < 0)
    {
        result.AddError("skip must be >= 0");
    }
});
var nodesListTakeOpt = new Option<int?>("--take") { Description = "Number of items to take (pagination)" };
nodesListTakeOpt.Validators.Add(result =>
{
    var value = result.GetValueOrDefault<int?>();
    if (value.HasValue && value.Value <= 0)
    {
        result.AddError("take must be > 0");
    }
});
nodesListCmd.Options.Add(nodesListOrgOpt);
nodesListCmd.Options.Add(nodesListStatusOpt);
nodesListCmd.Options.Add(nodesListSkipOpt);
nodesListCmd.Options.Add(nodesListTakeOpt);
nodesListCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(nodesListOrgOpt);
    var status = parseResult.GetValue(nodesListStatusOpt);
    var skip = parseResult.GetValue(nodesListSkipOpt);
    var take = parseResult.GetValue(nodesListTakeOpt);
    await NodesListNodesCommand.ExecuteAsync(orgId, status, skip, take, CancellationToken.None);
});

var nodesGetCmd = new Command("get", "Get node details");
var nodesGetNodeIdArg = new Argument<string>("node-id") { Description = "Node ID" };
var nodesGetOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
nodesGetCmd.Arguments.Add(nodesGetNodeIdArg);
nodesGetCmd.Options.Add(nodesGetOrgOpt);
nodesGetCmd.SetAction(async (parseResult, _) =>
{
    var nodeId = parseResult.GetValue(nodesGetNodeIdArg)!;
    var orgId = parseResult.GetValue(nodesGetOrgOpt);
    await NodesGetNodeCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
});

var nodesUpdateCmd = new Command("update", "Update node properties");
var nodesUpdateNodeIdArg = new Argument<string>("node-id") { Description = "Node ID" };
var nodesUpdateDisplayNameOpt = new Option<string?>("--display-name") { Description = "Display name for the node" };
var nodesUpdateOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
nodesUpdateCmd.Arguments.Add(nodesUpdateNodeIdArg);
nodesUpdateCmd.Options.Add(nodesUpdateDisplayNameOpt);
nodesUpdateCmd.Options.Add(nodesUpdateOrgOpt);
nodesUpdateCmd.SetAction(async (parseResult, _) =>
{
    var nodeId = parseResult.GetValue(nodesUpdateNodeIdArg)!;
    var displayName = parseResult.GetValue(nodesUpdateDisplayNameOpt);
    var orgId = parseResult.GetValue(nodesUpdateOrgOpt);
    await NodesUpdateNodeCommand.ExecuteAsync(nodeId, displayName, orgId, CancellationToken.None);
});

var nodesDecommissionCmd = new Command("decommission", "Decommission a node (permanent)");
var nodesDecommissionNodeIdArg = new Argument<string>("node-id") { Description = "Node ID" };
var nodesDecommissionOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
var nodesDecommissionForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
nodesDecommissionCmd.Arguments.Add(nodesDecommissionNodeIdArg);
nodesDecommissionCmd.Options.Add(nodesDecommissionOrgOpt);
nodesDecommissionCmd.Options.Add(nodesDecommissionForceOpt);
nodesDecommissionCmd.SetAction(async (parseResult, _) =>
{
    var nodeId = parseResult.GetValue(nodesDecommissionNodeIdArg)!;
    var orgId = parseResult.GetValue(nodesDecommissionOrgOpt);
    var force = parseResult.GetValue(nodesDecommissionForceOpt);
    await NodesDecommissionNodeCommand.ExecuteAsync(nodeId, orgId, force, CancellationToken.None);
});

var nodesMaintenanceCmd = new Command("maintenance", "Node maintenance mode commands");

var nodesMaintenanceStartCmd = new Command("start", "Put node into maintenance mode");
var nodesMaintenanceStartNodeIdArg = new Argument<string>("node-id") { Description = "Node ID" };
var nodesMaintenanceStartOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
nodesMaintenanceStartCmd.Arguments.Add(nodesMaintenanceStartNodeIdArg);
nodesMaintenanceStartCmd.Options.Add(nodesMaintenanceStartOrgOpt);
nodesMaintenanceStartCmd.SetAction(async (parseResult, _) =>
{
    var nodeId = parseResult.GetValue(nodesMaintenanceStartNodeIdArg)!;
    var orgId = parseResult.GetValue(nodesMaintenanceStartOrgOpt);
    await NodesEnterMaintenanceCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
});

var nodesMaintenanceStopCmd = new Command("stop", "Take node out of maintenance mode");
var nodesMaintenanceStopNodeIdArg = new Argument<string>("node-id") { Description = "Node ID" };
var nodesMaintenanceStopOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
nodesMaintenanceStopCmd.Arguments.Add(nodesMaintenanceStopNodeIdArg);
nodesMaintenanceStopCmd.Options.Add(nodesMaintenanceStopOrgOpt);
nodesMaintenanceStopCmd.SetAction(async (parseResult, _) =>
{
    var nodeId = parseResult.GetValue(nodesMaintenanceStopNodeIdArg)!;
    var orgId = parseResult.GetValue(nodesMaintenanceStopOrgOpt);
    await NodesExitMaintenanceCommand.ExecuteAsync(nodeId, orgId, CancellationToken.None);
});

nodesMaintenanceCmd.Subcommands.Add(nodesMaintenanceStartCmd);
nodesMaintenanceCmd.Subcommands.Add(nodesMaintenanceStopCmd);

nodesCmd.Subcommands.Add(nodesListCmd);
nodesCmd.Subcommands.Add(nodesGetCmd);
nodesCmd.Subcommands.Add(nodesUpdateCmd);
nodesCmd.Subcommands.Add(nodesDecommissionCmd);
nodesCmd.Subcommands.Add(nodesMaintenanceCmd);

// ============================================================================
// ENROLLMENT COMMANDS
// ============================================================================

var enrollmentCmd = new Command("enrollment", "Agent enrollment token management");

var enrollmentTokensCmd = new Command("tokens", "Enrollment token management");

var enrollmentTokensCreateCmd = new Command("create", "Create a new enrollment token");
var enrollmentTokensCreateLabelOpt = new Option<string?>("--label") { Description = "Label for the token" };
var enrollmentTokensCreateExpiresOpt = new Option<int?>("--expires") { Description = "Token expiration in minutes (default: 60)" };
var enrollmentTokensCreateOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
enrollmentTokensCreateCmd.Options.Add(enrollmentTokensCreateLabelOpt);
enrollmentTokensCreateCmd.Options.Add(enrollmentTokensCreateExpiresOpt);
enrollmentTokensCreateCmd.Options.Add(enrollmentTokensCreateOrgOpt);
enrollmentTokensCreateCmd.SetAction(async (parseResult, _) =>
{
    var label = parseResult.GetValue(enrollmentTokensCreateLabelOpt);
    var expires = parseResult.GetValue(enrollmentTokensCreateExpiresOpt);
    var orgId = parseResult.GetValue(enrollmentTokensCreateOrgOpt);
    await NodesCreateTokenCommand.ExecuteAsync(label, expires, orgId, CancellationToken.None);
});

var enrollmentTokensListCmd = new Command("list", "List active enrollment tokens");
var enrollmentTokensListOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
enrollmentTokensListCmd.Options.Add(enrollmentTokensListOrgOpt);
enrollmentTokensListCmd.SetAction(async (parseResult, _) =>
{
    var orgId = parseResult.GetValue(enrollmentTokensListOrgOpt);
    await NodesListTokensCommand.ExecuteAsync(orgId, CancellationToken.None);
});

var enrollmentTokensRevokeCmd = new Command("revoke", "Revoke an enrollment token");
var enrollmentTokensRevokeIdArg = new Argument<string>("token-id") { Description = "Token ID to revoke" };
var enrollmentTokensRevokeOrgOpt = new Option<string?>("--org") { Description = "Organization ID (defaults to current org)" };
var enrollmentTokensRevokeForceOpt = new Option<bool>("--force") { Description = "Skip confirmation prompt" };
enrollmentTokensRevokeCmd.Arguments.Add(enrollmentTokensRevokeIdArg);
enrollmentTokensRevokeCmd.Options.Add(enrollmentTokensRevokeOrgOpt);
enrollmentTokensRevokeCmd.Options.Add(enrollmentTokensRevokeForceOpt);
enrollmentTokensRevokeCmd.SetAction(async (parseResult, _) =>
{
    var tokenId = parseResult.GetValue(enrollmentTokensRevokeIdArg)!;
    var orgId = parseResult.GetValue(enrollmentTokensRevokeOrgOpt);
    var force = parseResult.GetValue(enrollmentTokensRevokeForceOpt);
    await NodesRevokeTokenCommand.ExecuteAsync(tokenId, orgId, force, CancellationToken.None);
});

enrollmentTokensCmd.Subcommands.Add(enrollmentTokensCreateCmd);
enrollmentTokensCmd.Subcommands.Add(enrollmentTokensListCmd);
enrollmentTokensCmd.Subcommands.Add(enrollmentTokensRevokeCmd);

enrollmentCmd.Subcommands.Add(enrollmentTokensCmd);

// ============================================================================
// NOTIFICATIONS COMMANDS
// ============================================================================

var notificationsCmd = new Command("notifications", "Notification service commands");

var notificationsLogsCmd = new Command("logs", "List notification logs");
var notificationsLimitOpt = new Option<int?>("--limit") { Description = "Maximum number of logs to return (default 50, max 100)" };
var notificationsStatusOpt = new Option<string?>("--status") { Description = "Filter by status (sent, failed, pending)" };
var notificationsOrgOpt = new Option<Guid?>("--org") { Description = "Filter by organization ID" };
notificationsLogsCmd.Options.Add(notificationsLimitOpt);
notificationsLogsCmd.Options.Add(notificationsStatusOpt);
notificationsLogsCmd.Options.Add(notificationsOrgOpt);
notificationsLogsCmd.SetAction(async (parseResult, cancellationToken) =>
{
    var limit = parseResult.GetValue(notificationsLimitOpt);
    var status = parseResult.GetValue(notificationsStatusOpt);
    var orgId = parseResult.GetValue(notificationsOrgOpt);
    return await ListLogsCommand.ExecuteAsync(limit, status, orgId, cancellationToken);
});

notificationsCmd.Subcommands.Add(notificationsLogsCmd);

var notificationsTestCmd = new Command("test", "Send a test notification to Discord");
var notificationsTestTitleOpt = new Option<string?>("--title") { Description = "Notification title" };
var notificationsTestMessageOpt = new Option<string?>("--message") { Description = "Notification message" };
var notificationsTestSeverityOpt = new Option<string?>("--severity") { Description = "Severity level (Info, Warning, Error, Critical)" };
var notificationsTestOrgOpt = new Option<Guid?>("--org") { Description = "Organization ID" };
notificationsTestCmd.Options.Add(notificationsTestTitleOpt);
notificationsTestCmd.Options.Add(notificationsTestMessageOpt);
notificationsTestCmd.Options.Add(notificationsTestSeverityOpt);
notificationsTestCmd.Options.Add(notificationsTestOrgOpt);
notificationsTestCmd.SetAction(async (parseResult, cancellationToken) =>
{
    var title = parseResult.GetValue(notificationsTestTitleOpt);
    var message = parseResult.GetValue(notificationsTestMessageOpt);
    var severity = parseResult.GetValue(notificationsTestSeverityOpt);
    var orgId = parseResult.GetValue(notificationsTestOrgOpt);
    return await SendTestCommand.ExecuteAsync(title, message, severity, orgId, cancellationToken);
});
notificationsCmd.Subcommands.Add(notificationsTestCmd);

// ============================================================================
// DISCORD COMMANDS
// ============================================================================

var discordCmd = new Command("discord", "Discord service commands");

var discordStatusCmd = new Command("status", "Check Discord bot status and platform health");
discordStatusCmd.SetAction(async (parseResult, cancellationToken) =>
{
    return await DiscordStatusCommand.ExecuteAsync(cancellationToken);
});

discordCmd.Subcommands.Add(discordStatusCmd);

var discordChannelsCmd = new Command("channels", "List Discord channels the bot can see");
var discordGuildIdOpt = new Option<ulong?>("--guild") { Description = "Filter by specific guild ID" };
discordChannelsCmd.Options.Add(discordGuildIdOpt);
discordChannelsCmd.SetAction(async (parseResult, cancellationToken) =>
{
    var guildId = parseResult.GetValue(discordGuildIdOpt);
    return await ChannelsCommand.ExecuteAsync(guildId, cancellationToken);
});
discordCmd.Subcommands.Add(discordChannelsCmd);

// ============================================================================
// COMMANDS LIST
// ============================================================================

var commandsCmd = new Command("commands", "List available commands and usage");
commandsCmd.SetAction(async (_, _) =>
{
    await CommandsCommand.ExecuteAsync(root);
});

// ============================================================================
// VERSION COMMAND
// ============================================================================

var versionCmd = new Command("version", "Show CLI build and breaking change info");
versionCmd.SetAction(async (_, _) =>
{
    await VersionCommand.ExecuteAsync();
});

// ============================================================================
// LEGACY PING COMMAND (keeping for backwards compatibility)
// ============================================================================

var urlOpt = new Option<string>("--url")
{
    Description = "Health endpoint URL",
    DefaultValueFactory = _ => "http://localhost:5000/healthz",
};

var ping = new Command("ping", "Ping a service health endpoint (legacy command, prefer 'gateway health')");
ping.Options.Add(urlOpt);

ping.SetAction(async (parseResult, cancellationToken) =>
{
    var url = parseResult.GetValue(urlOpt)!;
    using var http = new HttpClient();
    var res = await http.GetAsync(url, cancellationToken);
    var body = await res.Content.ReadAsStringAsync(cancellationToken);

    AnsiConsole.MarkupLine($"[bold]{(int)res.StatusCode}[/] {res.ReasonPhrase}");
    if (!string.IsNullOrWhiteSpace(body))
        AnsiConsole.MarkupLine($"[dim]{body}[/]");
});

// ============================================================================
// REGISTER ALL COMMANDS
// ============================================================================

root.Subcommands.Add(authCmd);
root.Subcommands.Add(meCmd);
root.Subcommands.Add(identityCmd);
root.Subcommands.Add(memberCmd);
root.Subcommands.Add(secretCmd);
root.Subcommands.Add(keyvaultCmd);
root.Subcommands.Add(gatewayCmd);
root.Subcommands.Add(nodesCmd);
root.Subcommands.Add(enrollmentCmd);
root.Subcommands.Add(notificationsCmd);
root.Subcommands.Add(discordCmd);
root.Subcommands.Add(commandsCmd);
root.Subcommands.Add(versionCmd);
root.Subcommands.Add(ping);

return await root.Parse(args).InvokeAsync();
