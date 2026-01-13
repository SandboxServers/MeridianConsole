using System.CommandLine;
using Dhadgar.Cli.Commands.Auth;
using Dhadgar.Cli.Commands.Gateway;
using Dhadgar.Cli.Commands.Member;
using Dhadgar.Cli.Commands.Org;
using Dhadgar.Cli.Commands.Secret;
using Dhadgar.Cli.Commands.KeyVault;
using Spectre.Console;
using IdentityListOrgsCommand = Dhadgar.Cli.Commands.Identity.ListOrgsCommand;
using IdentityGetOrgCommand = Dhadgar.Cli.Commands.Identity.GetOrgCommand;
using IdentityCreateOrgCommand = Dhadgar.Cli.Commands.Identity.CreateOrgCommand;
using IdentityUpdateOrgCommand = Dhadgar.Cli.Commands.Identity.UpdateOrgCommand;
using IdentityDeleteOrgCommand = Dhadgar.Cli.Commands.Identity.DeleteOrgCommand;

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
    table.AddRow("[cyan]dhadgar identity[/]", "[dim]Identity service commands (orgs CRUD)[/]");
    table.AddRow("[cyan]dhadgar org[/]", "[dim]Organization management (list, create, switch)[/]");
    table.AddRow("[cyan]dhadgar member[/]", "[dim]Member management (list, invite, remove)[/]");
    table.AddRow("[cyan]dhadgar secret[/]", "[dim]Secret management (get, set, rotate, certificates)[/]");
    table.AddRow("[cyan]dhadgar keyvault[/]", "[dim]Azure Key Vault management (list, create)[/]");
    table.AddRow("[cyan]dhadgar gateway[/]", "[dim]Gateway diagnostics (health check)[/]");

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
var identityUrlOpt = new Option<string?>("--identity-url", "Identity service URL");
authLoginCmd.AddOption(clientIdOpt);
authLoginCmd.AddOption(clientSecretOpt);
authLoginCmd.AddOption(identityUrlOpt);
authLoginCmd.SetHandler(async (string? clientId, string? clientSecret, string? identityUrl) =>
{
    await LoginCommand.ExecuteAsync(clientId, clientSecret, identityUrl, CancellationToken.None);
}, clientIdOpt, clientSecretOpt, identityUrlOpt);

var authStatusCmd = new Command("status", "Show authentication status and configuration");
authStatusCmd.SetHandler(async () =>
{
    await StatusCommand.ExecuteAsync(CancellationToken.None);
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

identityOrgsCmd.AddCommand(identityOrgsListCmd);
identityOrgsCmd.AddCommand(identityOrgsGetCmd);
identityOrgsCmd.AddCommand(identityOrgsCreateCmd);
identityOrgsCmd.AddCommand(identityOrgsUpdateCmd);
identityOrgsCmd.AddCommand(identityOrgsDeleteCmd);

identityCmd.AddCommand(identityOrgsCmd);

// ============================================================================
// ORG COMMANDS
// ============================================================================

var orgCmd = new Command("org", "Organization management");

var orgListCmd = new Command("list", "List your organizations");
orgListCmd.SetHandler(async () =>
{
    await ListOrgsCommand.ExecuteAsync(CancellationToken.None);
});

var orgCreateCmd = new Command("create", "Create a new organization");
var orgNameArg = new Argument<string?>("name", "Organization name (optional, will prompt)") { Arity = ArgumentArity.ZeroOrOne };
orgCreateCmd.AddArgument(orgNameArg);
orgCreateCmd.SetHandler(async (string? name) =>
{
    await CreateOrgCommand.ExecuteAsync(name, CancellationToken.None);
}, orgNameArg);

var orgSwitchCmd = new Command("switch", "Switch to a different organization");
var orgIdArg = new Argument<string>("org-id", "Organization ID to switch to");
orgSwitchCmd.AddArgument(orgIdArg);
orgSwitchCmd.SetHandler(async (string orgId) =>
{
    await SwitchOrgCommand.ExecuteAsync(orgId, CancellationToken.None);
}, orgIdArg);

orgCmd.AddCommand(orgListCmd);
orgCmd.AddCommand(orgCreateCmd);
orgCmd.AddCommand(orgSwitchCmd);

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

keyvaultCmd.AddCommand(vaultListCmd);
keyvaultCmd.AddCommand(vaultGetCmd);
keyvaultCmd.AddCommand(vaultCreateCmd);
keyvaultCmd.AddCommand(vaultUpdateCmd);

// ============================================================================
// GATEWAY COMMANDS
// ============================================================================

var gatewayCmd = new Command("gateway", "Gateway and infrastructure diagnostics");

var gatewayHealthCmd = new Command("health", "Check health of all services");
gatewayHealthCmd.SetHandler(async () =>
{
    await HealthCommand.ExecuteAsync(CancellationToken.None);
});

gatewayCmd.AddCommand(gatewayHealthCmd);

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
root.AddCommand(identityCmd);
root.AddCommand(orgCmd);
root.AddCommand(memberCmd);
root.AddCommand(secretCmd);
root.AddCommand(keyvaultCmd);
root.AddCommand(gatewayCmd);
root.AddCommand(ping);

return await root.InvokeAsync(args);
