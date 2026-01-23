# Dhadgar CLI

Beautiful command-line interface for **Meridian Console** -- the modern game server control plane.

Built with [Spectre.Console](https://spectreconsole.net/) for gorgeous terminal UI with colors, tables, spinners, and interactive prompts.

---

## Table of Contents

1. [Overview](#overview)
2. [Features](#features)
3. [Tech Stack](#tech-stack)
4. [Installation](#installation)
5. [Configuration](#configuration)
6. [Architecture](#architecture)
7. [Commands Reference](#commands-reference)
   - [Authentication Commands](#authentication-commands)
   - [Self-Service Commands](#self-service-commands-me)
   - [Identity Commands](#identity-commands)
   - [Member Commands](#member-commands)
   - [Secret Commands](#secret-commands)
   - [Key Vault Commands](#key-vault-commands)
   - [Gateway Commands](#gateway-commands)
   - [Utility Commands](#utility-commands)
8. [API Clients](#api-clients)
9. [Authentication Flow](#authentication-flow)
10. [Output Formatting](#output-formatting)
11. [Error Handling](#error-handling)
12. [Exit Codes](#exit-codes)
13. [Testing](#testing)
14. [Development Guide](#development-guide)
15. [Troubleshooting](#troubleshooting)
16. [Environment Variables](#environment-variables)
17. [Security Considerations](#security-considerations)
18. [Backend Implementation Status](#backend-implementation-status)
19. [Related Documentation](#related-documentation)

---

## Overview

The `dhadgar` CLI is the primary command-line interface for interacting with Meridian Console services. It provides a unified way to:

- **Authenticate** with the Identity service using OAuth client credentials flow
- **Manage organizations** -- create, list, switch between, search, and delete organizations
- **Manage users and roles** -- full CRUD operations on users, roles, and role assignments
- **Handle secrets** -- get, set, rotate, and delete secrets stored in Azure Key Vault
- **Manage certificates** -- list and import TLS certificates
- **Monitor infrastructure** -- health checks across all backend services
- **Debug gateway configuration** -- inspect YARP routes and clusters

The CLI is designed for both interactive use (with rich terminal UI, interactive prompts, and spinners) and scripting (with JSON output for identity commands).

### Role in the Platform

```
+----------------+     +----------------+     +------------------+
|                |     |                |     |                  |
|   dhadgar CLI  +---->+   Gateway      +---->+  Backend         |
|                |     |   (YARP)       |     |  Services        |
+----------------+     +----------------+     +------------------+
                              |
                              v
                   +---------------------+
                   |  Identity, Secrets, |
                   |  Nodes, Servers...  |
                   +---------------------+
```

The CLI communicates with backend services through the Gateway (YARP reverse proxy) or directly to services during development. All API calls use typed Refit interfaces with automatic Bearer token authentication.

---

## Features

| Feature | Description |
|---------|-------------|
| Beautiful UI | Rich terminal output with colors, tables, panels, and status indicators |
| Authentication | OAuth client credentials flow with automatic token management |
| Organization Management | Create, list, switch between, search, and delete organizations |
| User Management | Full CRUD operations on users within organizations |
| Role-Based Access | Create roles, assign/revoke roles, manage permissions |
| Claim Management | Grant and revoke fine-grained permissions to members |
| Secret Management | Secure access to OAuth, BetterAuth, and infrastructure secrets |
| Certificate Management | List and import TLS certificates to Azure Key Vault |
| Health Monitoring | Real-time service health checks with response times |
| Gateway Diagnostics | Inspect YARP routes, clusters, and backend service health |
| Session Management | View and revoke active sessions |
| JSON Output | Machine-readable output for scripting (identity commands) |
| Interactive Prompts | User-friendly prompts for sensitive input |
| Cross-Platform | Works on Windows, Linux, and macOS |

---

## Tech Stack

### Core Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `System.CommandLine` | 2.0.0-beta5 | CLI framework with argument parsing, help generation |
| `Spectre.Console` | 0.49.1 | Rich terminal UI (tables, panels, colors, spinners) |
| `Spectre.Console.Cli` | 0.49.1 | CLI command infrastructure |
| `Refit` | 8.0.0 | Type-safe REST API clients |

### Project Dependencies

| Project | Purpose |
|---------|---------|
| `Dhadgar.Contracts` | Shared DTOs and message contracts |

### Runtime

- **.NET 10.0** (pinned in `global.json`)
- Modern C# with nullable reference types enabled

### Build Configuration

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>dhadgar</ToolCommandName>
<PackageId>Dhadgar.Cli</PackageId>
```

The CLI is packaged as a .NET global tool, allowing installation via `dotnet tool install`.

---

## Installation

### Global Tool Installation (Recommended)

Build the CLI in Release mode to automatically install it as a global tool:

```bash
# From repository root
dotnet build src/Dhadgar.Cli -c Release
```

This automatically:
1. Builds the CLI project
2. Packs it as a NuGet package
3. Uninstalls any existing version
4. Installs it as the global tool `dhadgar`

Verify installation:

```bash
dhadgar --version
dhadgar --help
```

### Run Without Installation

For development or one-off usage:

```bash
# Run directly via dotnet
dotnet run --project src/Dhadgar.Cli -- <command>

# Example: check authentication status
dotnet run --project src/Dhadgar.Cli -- auth status
```

### Build and Publish

```bash
# Build only (Debug)
dotnet build src/Dhadgar.Cli

# Build only (Release, triggers auto-install)
dotnet build src/Dhadgar.Cli -c Release

# Publish as standalone executable
dotnet publish src/Dhadgar.Cli -c Release -o ./publish
```

### CI/CD Note

The auto-install feature is skipped during CI/CD builds by passing:

```bash
dotnet build src/Dhadgar.Cli -c Release /p:SkipGlobalToolInstall=true
```

This prevents the build from attempting to install a global tool in the pipeline environment.

---

## Configuration

### Configuration File Location

Configuration is stored in `~/.dhadgar/config.json`:

- **Windows**: `C:\Users\<username>\.dhadgar\config.json`
- **Linux/macOS**: `/home/<username>/.dhadgar/config.json`

The file is created automatically on first run with default values.

### Configuration Options

```json
{
  "gateway_url": "http://localhost:5000",
  "identity_url": "http://localhost:5001",
  "secrets_url": "http://localhost:5002",
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "refresh_token": "rt_abc123...",
  "current_org_id": "550e8400-e29b-41d4-a716-446655440000",
  "token_expires_at": "2026-01-22T15:30:00Z"
}
```

| Field | Description | Default |
|-------|-------------|---------|
| `gateway_url` | Base URL for the YARP Gateway | `http://localhost:5000` |
| `identity_url` | Direct URL for Identity service (dev mode) | `http://localhost:5001` |
| `secrets_url` | Direct URL for Secrets service (dev mode) | `http://localhost:5002` |
| `access_token` | Current OAuth access token | (none) |
| `refresh_token` | OAuth refresh token for token renewal | (none) |
| `current_org_id` | Currently selected organization ID | (none) |
| `token_expires_at` | Token expiration timestamp (UTC) | (none) |

### Computed URLs

The CLI computes effective URLs based on configuration:

```csharp
// Routes through gateway by default
EffectiveIdentityUrl = IdentityUrl ?? $"{GatewayUrl}/api/v1/identity"
EffectiveSecretsUrl = SecretsUrl ?? $"{GatewayUrl}/api/v1/secrets"
```

For local development, you can override URLs to bypass the gateway:

```json
{
  "gateway_url": "http://localhost:5000",
  "identity_url": "http://localhost:5001",
  "secrets_url": "http://localhost:5002"
}
```

### Authentication State

The CLI tracks authentication state with:

```csharp
public bool IsAuthenticated()
{
    return !string.IsNullOrWhiteSpace(AccessToken) &&
           TokenExpiresAt.HasValue &&
           TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(1);
}
```

Tokens are considered valid if they exist and have at least 1 minute remaining before expiration.

---

## Architecture

### Project Structure

```
Dhadgar.Cli/
├── Commands/                    # Command implementations
│   ├── Auth/                    # Authentication commands
│   │   ├── LoginCommand.cs      # OAuth login flow
│   │   ├── LogoutCommand.cs     # Clear tokens
│   │   └── StatusCommand.cs     # Show auth status
│   ├── Gateway/                 # Gateway diagnostics
│   │   ├── ClustersCommand.cs   # List YARP clusters
│   │   ├── HealthCommand.cs     # Service health checks
│   │   ├── RoutesCommand.cs     # List YARP routes
│   │   └── ServicesCommand.cs   # All services health
│   ├── Help/                    # Help commands
│   │   └── CommandsCommand.cs   # List all commands
│   ├── Identity/                # Identity service commands
│   │   ├── AssignRoleCommand.cs
│   │   ├── CreateOrgCommand.cs
│   │   ├── CreateRoleCommand.cs
│   │   ├── CreateUserCommand.cs
│   │   ├── DeleteOrgCommand.cs
│   │   ├── DeleteRoleCommand.cs
│   │   ├── DeleteUserCommand.cs
│   │   ├── GetOrgCommand.cs
│   │   ├── GetRoleCommand.cs
│   │   ├── GetUserCommand.cs
│   │   ├── GrantClaimCommand.cs
│   │   ├── IdentityCommandHelpers.cs  # Shared utilities
│   │   ├── ListClaimsCommand.cs
│   │   ├── ListOrgsCommand.cs
│   │   ├── ListRoleMembersCommand.cs
│   │   ├── ListRolesCommand.cs
│   │   ├── ListUsersCommand.cs
│   │   ├── RevokeClaimCommand.cs
│   │   ├── RevokeRoleCommand.cs
│   │   ├── SearchOrgsCommand.cs
│   │   ├── SearchRolesCommand.cs
│   │   ├── SearchUsersCommand.cs
│   │   ├── SwitchOrgCommand.cs
│   │   ├── UpdateOrgCommand.cs
│   │   ├── UpdateRoleCommand.cs
│   │   └── UpdateUserCommand.cs
│   ├── KeyVault/                # Azure Key Vault commands
│   │   ├── CreateVaultCommand.cs
│   │   ├── DeleteVaultCommand.cs
│   │   ├── GetVaultCommand.cs
│   │   ├── ListVaultsCommand.cs
│   │   └── UpdateVaultCommand.cs
│   ├── Me/                      # Self-service commands
│   │   ├── GetProfileCommand.cs
│   │   ├── ListLinkedAccountsCommand.cs
│   │   ├── ListOrganizationsCommand.cs
│   │   ├── ListPermissionsCommand.cs
│   │   ├── ListSessionsCommand.cs
│   │   ├── RevokeAllSessionsCommand.cs
│   │   ├── RevokeSessionCommand.cs
│   │   └── UpdateProfileCommand.cs
│   ├── Member/                  # Member commands
│   │   └── ListMembersCommand.cs
│   ├── Secret/                  # Secret management
│   │   ├── DeleteSecretCommand.cs
│   │   ├── GetSecretCommand.cs
│   │   ├── ImportCertificateCommand.cs
│   │   ├── ListCertificatesCommand.cs
│   │   ├── ListSecretsCommand.cs
│   │   ├── RotateSecretCommand.cs
│   │   └── SetSecretCommand.cs
│   ├── Version/                 # Version info
│   │   └── VersionCommand.cs
│   └── CommandValidation.cs     # Input validation
├── Configuration/               # Config file management
│   └── CliConfig.cs             # Load/save config
├── Infrastructure/              # API clients
│   └── Clients/
│       ├── ApiClientFactory.cs  # Creates typed API clients
│       ├── IGatewayApi.cs       # Gateway API interface
│       ├── IHealthApi.cs        # Health check interface
│       ├── IIdentityApi.cs      # Identity API interface
│       ├── IKeyVaultApi.cs      # Key Vault API interface
│       └── ISecretsApi.cs       # Secrets API interface
├── Utilities/                   # Utility classes
│   └── ExpirationParser.cs      # Parse expiration strings
├── Hello.cs                     # Smoke test surface
├── Program.cs                   # Entry point and command wiring
├── Dhadgar.Cli.csproj           # Project file
└── README.md                    # This file
```

### Command Pattern

Commands follow a consistent pattern:

```csharp
public sealed class ExampleCommand
{
    public static async Task<int> ExecuteAsync(
        string argument,
        CancellationToken ct)
    {
        // 1. Load configuration
        var config = CliConfig.Load();

        // 2. Check authentication (if required)
        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/]");
            return 1;
        }

        // 3. Create API client factory
        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        // 4. Get typed API client
        var api = factory.CreateIdentityClient();

        // 5. Execute API call with spinner
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Working...", async ctx =>
            {
                try
                {
                    var result = await api.SomeMethodAsync(ct);
                    // Display result
                }
                catch (ApiException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                }
            });

        return 0;
    }
}
```

### Command Registration

Commands are registered in `Program.cs` using System.CommandLine:

```csharp
var root = new RootCommand("Dhadgar CLI");

// Create command group
var authCmd = new Command("auth", "Authentication commands");

// Create subcommand with options
var loginCmd = new Command("login", "Authenticate with Identity service");
var clientIdOpt = new Option<string?>("--client-id", "Client ID");
loginCmd.AddOption(clientIdOpt);

// Set handler
loginCmd.SetHandler(async (string? clientId) =>
{
    await LoginCommand.ExecuteAsync(clientId, CancellationToken.None);
}, clientIdOpt);

// Build hierarchy
authCmd.AddCommand(loginCmd);
root.AddCommand(authCmd);

return await root.InvokeAsync(args);
```

---

## Commands Reference

### Authentication Commands

#### `dhadgar auth login`

Authenticate with the Identity service using OAuth client credentials flow.

```bash
# Interactive login (prompts for credentials)
dhadgar auth login

# Login with explicit credentials
dhadgar auth login --client-id dev-client --client-secret dev-secret

# Login with custom Identity URL
dhadgar auth login --identity-url http://identity.example.com
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--client-id` | string | OAuth client ID (defaults to `dev-client`) |
| `--client-secret` | string | OAuth client secret (defaults to `dev-secret`) |
| `--identity-url` | Uri | Override Identity service URL |

**Behavior:**
- If credentials are not provided, prompts interactively
- Client secret is hidden during interactive input
- Stores tokens in `~/.dhadgar/config.json`
- Shows success panel with token expiration time

#### `dhadgar auth status`

Display current authentication status and configuration.

```bash
dhadgar auth status
```

**Output:**
```
+----------------+-------------------------+
| Setting        | Value                   |
+----------------+-------------------------+
| Gateway URL    | http://localhost:5000   |
| Identity URL   | http://localhost:5001   |
| Secrets URL    | http://localhost:5002   |
| Current Org ID | 550e8400-e29b-41d4...   |
| Authentication | [green] Authenticated   |
| Token Expires  | 1/22/2026 3:30 PM       |
+----------------+-------------------------+
```

#### `dhadgar auth logout`

Clear authentication tokens and log out.

```bash
dhadgar auth logout
```

**Behavior:**
- Clears `access_token`, `refresh_token`, `token_expires_at`, and `current_org_id`
- Displays confirmation panel
- Returns 0 even if not authenticated

---

### Self-Service Commands (me)

Commands for the currently authenticated user to manage their own profile.

#### `dhadgar me profile`

Get your profile information.

```bash
dhadgar me profile
```

**Output (JSON):**
```json
{
  "id": "user-123",
  "email": "user@example.com",
  "displayName": "John Doe",
  "emailVerified": true,
  "preferredOrganizationId": "org-456",
  "hasPasskeysRegistered": false,
  "createdAt": "2026-01-15T10:00:00Z",
  "lastAuthenticatedAt": "2026-01-22T08:30:00Z"
}
```

#### `dhadgar me update`

Update your profile information.

```bash
# Update display name
dhadgar me update --name "Jane Doe"

# Set preferred organization
dhadgar me update --preferred-org 550e8400-e29b-41d4-a716-446655440000

# Update both
dhadgar me update --name "Jane Doe" --preferred-org org-123
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--name` | string | New display name |
| `--preferred-org` | string | Preferred organization ID |

#### `dhadgar me orgs`

List organizations you belong to.

```bash
dhadgar me orgs
```

**Output (JSON):**
```json
{
  "organizations": [
    {
      "id": "org-123",
      "name": "My Organization",
      "slug": "my-org",
      "role": "owner",
      "joinedAt": "2026-01-01T00:00:00Z",
      "isPreferred": true
    }
  ]
}
```

#### `dhadgar me linked-accounts`

List your linked OAuth provider accounts.

```bash
dhadgar me linked-accounts
```

**Output (JSON):**
```json
{
  "linkedAccounts": [
    {
      "id": "link-123",
      "provider": "steam",
      "providerDisplayName": "Steam",
      "linkedAt": "2026-01-10T12:00:00Z",
      "lastUsedAt": "2026-01-22T08:00:00Z"
    }
  ]
}
```

#### `dhadgar me permissions`

List your permissions in the current organization.

```bash
dhadgar me permissions
```

**Output (JSON):**
```json
{
  "organizationId": "org-123",
  "permissions": [
    "servers:read",
    "servers:write",
    "nodes:manage",
    "secrets:read"
  ]
}
```

#### `dhadgar me sessions list`

List your active sessions.

```bash
dhadgar me sessions list
```

**Output (JSON):**
```json
[
  {
    "id": "session-123",
    "organizationId": "org-123",
    "deviceInfo": "Chrome on Windows",
    "issuedAt": "2026-01-22T08:00:00Z",
    "expiresAt": "2026-01-23T08:00:00Z",
    "isCurrent": true
  }
]
```

#### `dhadgar me sessions revoke <session-id>`

Revoke a specific session.

```bash
dhadgar me sessions revoke session-123
```

#### `dhadgar me sessions revoke-all`

Revoke all sessions (logout everywhere).

```bash
dhadgar me sessions revoke-all
```

**Output (JSON):**
```json
{
  "revokedCount": 5
}
```

---

### Identity Commands

Commands for managing organizations, users, roles, and permissions.

#### Organizations

##### `dhadgar identity orgs list`

List all organizations you have access to.

```bash
dhadgar identity orgs list
```

##### `dhadgar identity orgs get <org-id>`

Get detailed information about an organization.

```bash
dhadgar identity orgs get 550e8400-e29b-41d4-a716-446655440000
```

##### `dhadgar identity orgs create`

Create a new organization.

```bash
dhadgar identity orgs create --name "My New Org"
dhadgar identity orgs create --name "My New Org" --description "Team workspace"
```

**Options:**

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--name` | string | Yes | Organization name |
| `--description` | string | No | Organization description |

##### `dhadgar identity orgs update <org-id>`

Update an organization.

```bash
dhadgar identity orgs update org-123 --name "New Name"
dhadgar identity orgs update org-123 --description "Updated description"
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--name` | string | New organization name |
| `--description` | string | New description |

##### `dhadgar identity orgs delete <org-id>`

Delete an organization.

```bash
# With confirmation prompt
dhadgar identity orgs delete org-123

# Skip confirmation
dhadgar identity orgs delete org-123 --force
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--force` | bool | Skip confirmation prompt |

##### `dhadgar identity orgs switch <org-id>`

Switch to a different organization (updates tokens).

```bash
dhadgar identity orgs switch org-123
```

##### `dhadgar identity orgs search`

Search organizations by name.

```bash
dhadgar identity orgs search --query "gaming"
```

**Options:**

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--query` | string | Yes | Search query |

#### Users

##### `dhadgar identity users list`

List users in the current or specified organization.

```bash
dhadgar identity users list
dhadgar identity users list --org org-123
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--org` | string | Organization ID (defaults to current org) |

##### `dhadgar identity users get <user-id>`

Get detailed user information.

```bash
dhadgar identity users get user-123
dhadgar identity users get user-123 --org org-123
```

##### `dhadgar identity users create`

Create a new user in an organization.

```bash
dhadgar identity users create --email user@example.com --org org-123
dhadgar identity users create --email user@example.com --org org-123 --name "John Doe"
```

**Options:**

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--email` | string | Yes | User email address |
| `--org` | string | Yes | Organization ID |
| `--name` | string | No | Display name |

##### `dhadgar identity users update <user-id>`

Update a user.

```bash
dhadgar identity users update user-123 --email newemail@example.com
dhadgar identity users update user-123 --name "Jane Doe" --org org-123
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--email` | string | New email address |
| `--name` | string | New display name |
| `--org` | string | Organization ID |

##### `dhadgar identity users delete <user-id>`

Delete a user from an organization.

```bash
dhadgar identity users delete user-123
dhadgar identity users delete user-123 --force --org org-123
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--force` | bool | Skip confirmation prompt |
| `--org` | string | Organization ID |

##### `dhadgar identity users search`

Search users by email or name.

```bash
dhadgar identity users search --query "john"
dhadgar identity users search --query "john" --org org-123
```

#### Roles

##### `dhadgar identity roles list`

List roles in the current organization.

```bash
dhadgar identity roles list
dhadgar identity roles list --org org-123
```

##### `dhadgar identity roles get <role-id>`

Get role details including permissions.

```bash
dhadgar identity roles get role-123
```

##### `dhadgar identity roles create`

Create a new role.

```bash
dhadgar identity roles create --name "Server Admin" --org org-123
dhadgar identity roles create --name "Server Admin" --org org-123 \
  --description "Can manage game servers" \
  --permissions "servers:read,servers:write,nodes:manage"
```

**Options:**

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--name` | string | Yes | Role name |
| `--org` | string | Yes | Organization ID |
| `--description` | string | No | Role description |
| `--permissions` | string | No | Comma-separated permissions |

##### `dhadgar identity roles update <role-id>`

Update a role.

```bash
dhadgar identity roles update role-123 --name "New Name"
dhadgar identity roles update role-123 --permissions "servers:read,servers:write"
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--name` | string | New role name |
| `--description` | string | New description |
| `--permissions` | string | Comma-separated permissions |
| `--org` | string | Organization ID |

##### `dhadgar identity roles delete <role-id>`

Delete a role.

```bash
dhadgar identity roles delete role-123
dhadgar identity roles delete role-123 --org org-123
```

##### `dhadgar identity roles members <role-id>`

List users assigned to a role.

```bash
dhadgar identity roles members role-123
```

##### `dhadgar identity roles assign <role-id>`

Assign a role to a user.

```bash
dhadgar identity roles assign role-123 --user user-456
```

**Options:**

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--user` | string | Yes | User ID to assign |
| `--org` | string | No | Organization ID |

##### `dhadgar identity roles revoke <role-id>`

Revoke a role from a user.

```bash
dhadgar identity roles revoke role-123 --user user-456
```

##### `dhadgar identity roles search`

Search roles by name.

```bash
dhadgar identity roles search --query "admin"
```

#### Member Claims

Fine-grained permission management for organization members.

##### `dhadgar identity members claims <member-id>`

List custom claims for a member.

```bash
dhadgar identity members claims user-123
dhadgar identity members claims user-123 --org org-123
```

**Output (JSON):**
```json
{
  "memberId": "user-123",
  "claims": [
    {
      "id": "claim-456",
      "type": "grant",
      "value": "secrets:read:oauth",
      "expiresAt": "2026-02-22T00:00:00Z",
      "createdAt": "2026-01-22T10:00:00Z"
    }
  ]
}
```

##### `dhadgar identity members grant <member-id> <permission>`

Grant a permission to a member.

```bash
# Permanent grant
dhadgar identity members grant user-123 secrets:read:oauth

# Grant with expiration
dhadgar identity members grant user-123 secrets:read:oauth --expires 7d
dhadgar identity members grant user-123 secrets:read:oauth --expires 1h
dhadgar identity members grant user-123 secrets:read:oauth --expires "2026-12-31"
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `member-id` | User ID to grant permission to |
| `permission` | Permission to grant (e.g., `secrets:read:oauth`) |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--org` | string | Organization ID |
| `--expires` | string | Expiration (e.g., `1h`, `7d`, `30d`, `1w`, `1m`, or ISO 8601 date) |

**Expiration Formats:**
- `1h` - 1 hour
- `7d` - 7 days
- `2w` - 2 weeks
- `1m` - 1 month
- `2026-12-31` - ISO 8601 date

##### `dhadgar identity members revoke <member-id> <claim-id>`

Revoke a claim from a member.

```bash
dhadgar identity members revoke user-123 claim-456
dhadgar identity members revoke user-123 claim-456 --force
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--org` | string | Organization ID |
| `--force` | bool | Skip confirmation prompt |

---

### Member Commands

#### `dhadgar member list [org-id]`

List members of an organization.

```bash
# List members of current organization
dhadgar member list

# List members of specific organization
dhadgar member list org-123
```

---

### Secret Commands

Commands for managing secrets stored in Azure Key Vault.

#### `dhadgar secret get <name>`

Get a single secret by name.

```bash
# Get secret (masked by default)
dhadgar secret get Steam-ClientId

# Reveal actual value
dhadgar secret get Steam-ClientId --reveal

# Copy to clipboard (placeholder - not implemented)
dhadgar secret get Steam-ClientId --copy
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `name` | Secret name |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--reveal` | bool | Show actual secret value |
| `--copy` | bool | Copy to clipboard (not yet implemented) |

**Output (masked):**
```
+----------+------------------------------------+
| Name:    | Steam-ClientId                     |
| Value:   | ********************************   |
| Length:  | 32 characters                      |
+----------+------------------------------------+

Use --reveal to show actual value
```

#### `dhadgar secret list <category>`

List secrets by category.

```bash
# List OAuth provider secrets
dhadgar secret list oauth

# List BetterAuth secrets
dhadgar secret list betterauth

# List infrastructure secrets (database, messaging)
dhadgar secret list infrastructure

# Reveal all values
dhadgar secret list oauth --reveal
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `category` | Secret category: `oauth`, `betterauth`, `infrastructure` |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--reveal` | bool | Show actual secret values |

#### `dhadgar secret set <name> [value]`

Set or update a secret value.

```bash
# Set with explicit value
dhadgar secret set My-Secret "my-secret-value"

# Interactive prompt (value hidden)
dhadgar secret set My-Secret

# Read from stdin (useful for piping)
echo "my-secret-value" | dhadgar secret set My-Secret --stdin
cat secret.txt | dhadgar secret set My-Secret --stdin
```

**Arguments:**

| Argument | Required | Description |
|----------|----------|-------------|
| `name` | Yes | Secret name |
| `value` | No | Secret value (prompts if not provided) |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--stdin` | bool | Read value from stdin |

**Validation:**
- Maximum secret size: 25KB (Azure Key Vault limit)
- Large values should be stored in Azure Blob Storage

#### `dhadgar secret rotate <name>`

Rotate a secret (generate new value).

```bash
# With confirmation prompt
dhadgar secret rotate My-Secret

# Skip confirmation
dhadgar secret rotate My-Secret --force
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--force` | bool | Skip confirmation prompt |

#### `dhadgar secret delete <name>`

Delete a secret.

```bash
dhadgar secret delete My-Secret
dhadgar secret delete My-Secret --force
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--force` | bool | Skip confirmation prompt |

#### `dhadgar secret list-certs`

List certificates.

```bash
# List all certificates
dhadgar secret list-certs

# List certificates in specific vault
dhadgar secret list-certs --vault my-vault
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--vault` | string | Key Vault name |

#### `dhadgar secret import-cert <path>`

Import a certificate.

```bash
# Basic import (uses filename as name)
dhadgar secret import-cert /path/to/cert.pfx

# Import with custom name
dhadgar secret import-cert /path/to/cert.pfx --name my-cert

# Import password-protected certificate
dhadgar secret import-cert /path/to/cert.pfx --password secret123

# Import to specific vault
dhadgar secret import-cert /path/to/cert.pfx --vault my-vault
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `path` | Path to certificate file (.pfx, .p12, .pem, .cer) |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--name` | string | Certificate name (defaults to filename) |
| `--password` | string | Certificate password (for PFX/P12) |
| `--vault` | string | Target Key Vault name |

---

### Key Vault Commands

Commands for managing Azure Key Vaults.

#### `dhadgar keyvault list`

List all Key Vaults.

```bash
dhadgar keyvault list
```

#### `dhadgar keyvault get <name>`

Get detailed vault information.

```bash
dhadgar keyvault get my-vault
```

**Output includes:**
- Vault URI
- Location and resource group
- SKU (Standard/Premium)
- Secret, key, and certificate counts
- Soft delete and purge protection settings
- RBAC authorization status
- Network access settings
- Created/updated timestamps

#### `dhadgar keyvault create [name]`

Create a new Key Vault.

```bash
# Interactive (prompts for name)
dhadgar keyvault create

# With explicit name
dhadgar keyvault create my-vault

# With location
dhadgar keyvault create my-vault --location centralus
```

**Arguments:**

| Argument | Required | Description |
|----------|----------|-------------|
| `name` | No | Vault name (prompts if not provided) |

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--location` | string | Azure region |

**Name Requirements:**
- 3-24 characters
- Letters, numbers, and hyphens only
- Must start with a letter

#### `dhadgar keyvault update <name>`

Update Key Vault properties.

```bash
# Enable soft delete
dhadgar keyvault update my-vault --enable-soft-delete

# Disable soft delete
dhadgar keyvault update my-vault --disable-soft-delete

# Enable purge protection
dhadgar keyvault update my-vault --enable-purge-protection

# Set retention days
dhadgar keyvault update my-vault --retention-days 90

# Upgrade to premium
dhadgar keyvault update my-vault --sku premium
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--enable-soft-delete` | bool | Enable soft delete |
| `--disable-soft-delete` | bool | Disable soft delete |
| `--enable-purge-protection` | bool | Enable purge protection |
| `--disable-purge-protection` | bool | Disable purge protection |
| `--retention-days` | int | Soft delete retention (7-90 days) |
| `--sku` | string | Vault SKU (`standard` or `premium`) |

#### `dhadgar keyvault delete <name>`

Delete a Key Vault.

```bash
dhadgar keyvault delete my-vault
dhadgar keyvault delete my-vault --force
```

**Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--force` | bool | Skip confirmation prompt |

---

### Gateway Commands

Commands for monitoring gateway and backend service health.

#### `dhadgar gateway health`

Check health of core services (Gateway, Identity, Secrets).

```bash
dhadgar gateway health
```

**Output:**
```
+-----------+-----------+---------------+---------+
| Service   | Status    | Response Time | Message |
+-----------+-----------+---------------+---------+
| Gateway   | Healthy   | 53ms          | ok      |
| Identity  | Healthy   | 127ms         | ok      |
| Secrets   | Healthy   | 94ms          | ok      |
+-----------+-----------+---------------+---------+
```

Response time colors:
- Green: < 100ms
- Yellow: 100-500ms
- Red: > 500ms

#### `dhadgar gateway services`

List health status of all backend services (Development only).

```bash
dhadgar gateway services
```

Requires the Gateway's `/diagnostics/services` endpoint (Development mode only).

#### `dhadgar gateway routes`

List all YARP gateway routes (Development only).

```bash
dhadgar gateway routes
```

Shows:
- Route ID
- Cluster ID
- Path pattern
- Authorization policy
- Rate limiter policy
- Order

#### `dhadgar gateway clusters`

List all YARP clusters (Development only).

```bash
dhadgar gateway clusters
```

Shows:
- Cluster ID
- Available destinations
- Total destinations
- Health status

---

### Utility Commands

#### `dhadgar commands`

List all available commands with usage.

```bash
dhadgar commands
```

Displays a formatted table of all commands grouped by category with:
- Command path
- Usage syntax
- Description

#### `dhadgar version`

Show CLI build information.

```bash
dhadgar version
```

**Output:**
```
+---------------------------+---------------------------+
| Field                     | Value                     |
+---------------------------+---------------------------+
| Assembly Version          | 0.1.0.0                   |
| Build Date (UTC)          | 2026-01-22T10:30:00Z      |
| Last Breaking Change (UTC)| 2026-01-12T00:00:00Z      |
+---------------------------+---------------------------+
```

The `Last Breaking Change` date is embedded at build time and indicates when the last breaking change was introduced, helping users know if they need to update.

#### `dhadgar ping`

Legacy command for simple health checks.

```bash
# Default URL
dhadgar ping

# Custom URL
dhadgar ping --url http://localhost:5001/healthz
```

**Note:** Prefer `dhadgar gateway health` for comprehensive health checking.

---

## API Clients

The CLI uses [Refit](https://github.com/reactiveui/refit) for type-safe REST API clients.

### ApiClientFactory

Central factory for creating authenticated API clients:

```csharp
using var factory = ApiClientFactory.TryCreate(config, out var error);
if (factory is null)
{
    // Handle error
}

// Get typed clients
var identityApi = factory.CreateIdentityClient();
var secretsApi = factory.CreateSecretsClient();
var keyVaultApi = factory.CreateKeyVaultClient();
var gatewayApi = factory.CreateGatewayClient();
var healthApi = factory.CreateGatewayHealthClient();
```

**Features:**
- Automatic Bearer token authentication
- Certificate revocation checking enabled
- Proper URL normalization
- Resource cleanup via `IDisposable`

### IIdentityApi

Interface for Identity service operations:

```csharp
public interface IIdentityApi
{
    // Authentication
    Task<TokenResponse> GetTokenAsync(Dictionary<string, string> request);

    // Organizations
    Task<List<OrganizationResponse>> GetOrganizationsAsync();
    Task<OrganizationDetailResponse> GetOrganizationAsync(string orgId);
    Task<OrganizationDetailResponse> CreateOrganizationAsync(CreateOrganizationRequest request);
    Task<OrganizationDetailResponse> UpdateOrganizationAsync(string orgId, UpdateOrganizationRequest request);
    Task DeleteOrganizationAsync(string orgId);
    Task<SwitchOrganizationResponse> SwitchOrganizationAsync(string orgId);
    Task<List<OrganizationResponse>> SearchOrganizationsAsync(string query);

    // Members
    Task<List<MemberResponse>> GetMembersAsync(string orgId);
    Task<MemberClaimsResponse> GetMemberClaimsAsync(string orgId, string memberId);
    Task<AddClaimResponse> AddMemberClaimAsync(string orgId, string memberId, AddClaimRequest request);
    Task RemoveMemberClaimAsync(string orgId, string memberId, string claimId);

    // Users
    Task<List<UserSummaryResponse>> GetUsersAsync(string orgId);
    Task<UserDetailResponse> GetUserAsync(string orgId, string userId);
    Task<UserDetailResponse> CreateUserAsync(string orgId, CreateUserRequest request);
    Task<UserDetailResponse> UpdateUserAsync(string orgId, string userId, UpdateUserRequest request);
    Task DeleteUserAsync(string orgId, string userId);
    Task<List<UserSummaryResponse>> SearchUsersAsync(string orgId, string query);

    // Roles
    Task<List<RoleSummaryResponse>> GetRolesAsync(string orgId);
    Task<RoleSummaryResponse> GetRoleAsync(string orgId, string roleId);
    Task<RoleSummaryResponse> CreateRoleAsync(string orgId, CreateRoleRequest request);
    Task<RoleSummaryResponse> UpdateRoleAsync(string orgId, string roleId, UpdateRoleRequest request);
    Task DeleteRoleAsync(string orgId, string roleId);
    Task<List<RoleMemberResponse>> GetRoleMembersAsync(string orgId, string roleId);
    Task<RoleAssignmentResponse> AssignRoleAsync(string orgId, string roleId, RoleAssignmentRequest request);
    Task<RoleAssignmentResponse> RevokeRoleAsync(string orgId, string roleId, RoleAssignmentRequest request);
    Task<List<RoleSummaryResponse>> SearchRolesAsync(string orgId, string query);

    // Self-service (/me)
    Task<MeProfileResponse> GetMyProfileAsync();
    Task<MeProfileResponse> UpdateMyProfileAsync(UpdateProfileRequest request);
    Task<MyOrganizationsResponse> GetMyOrganizationsAsync();
    Task<MyLinkedAccountsResponse> GetMyLinkedAccountsAsync();
    Task<MyPermissionsResponse> GetMyPermissionsAsync();
    Task<List<SessionResponse>> GetMySessionsAsync();
    Task RevokeSessionAsync(string sessionId);
    Task<RevokeAllSessionsResponse> RevokeAllSessionsAsync();
    Task LogoutAsync();
}
```

### ISecretsApi

Interface for Secrets service operations:

```csharp
public interface ISecretsApi
{
    // Secrets
    Task<SecretResponse> GetSecretAsync(string name);
    Task<SecretsResponse> GetOAuthSecretsAsync();
    Task<SecretsResponse> GetBetterAuthSecretsAsync();
    Task<SecretsResponse> GetInfrastructureSecretsAsync();
    Task<SecretsResponse> GetSecretsBatchAsync(BatchSecretsRequest request);
    Task<SetSecretResponse> SetSecretAsync(string name, SetSecretRequest request);
    Task<RotateSecretResponse> RotateSecretAsync(string name);
    Task DeleteSecretAsync(string name);

    // Certificates
    Task<CertificateListResponse> GetCertificatesAsync();
    Task<CertificateListResponse> GetVaultCertificatesAsync(string vaultName);
    Task<CertificateImportResponse> ImportCertificateAsync(ImportCertificateRequest request);
    Task<CertificateImportResponse> ImportVaultCertificateAsync(string vaultName, ImportCertificateRequest request);
}
```

### IKeyVaultApi

Interface for Key Vault management:

```csharp
public interface IKeyVaultApi
{
    Task<KeyVaultListResponse> GetVaultsAsync();
    Task<KeyVaultResponse> GetVaultAsync(string vaultName);
    Task<KeyVaultResponse> CreateVaultAsync(CreateVaultRequest request);
    Task<KeyVaultResponse> UpdateVaultAsync(string vaultName, UpdateVaultRequest request);
    Task DeleteVaultAsync(string vaultName);
}
```

### IGatewayApi

Interface for Gateway diagnostics:

```csharp
public interface IGatewayApi
{
    Task<HealthResponse> GetHealthAsync();
    Task<ServiceHealthResponse> GetServiceHealthAsync(string service);
    Task<ReadinessResponse> GetReadinessAsync();
    Task<AllServicesHealthResponse> GetAllServicesHealthAsync();
    Task<RoutesInfoResponse> GetRoutesAsync();
    Task<ClustersInfoResponse> GetClustersAsync();
}
```

### IHealthApi

Lightweight health check interface:

```csharp
public interface IHealthApi
{
    Task<HealthStatusResponse?> GetHealthAsync();
}
```

---

## Authentication Flow

### OAuth Client Credentials Flow

The CLI uses OAuth 2.0 client credentials flow for machine-to-machine authentication:

```
+------------+          +----------------+          +----------------+
|            |  POST    |                |  POST    |                |
| dhadgar    +--------->+  Gateway       +--------->+  Identity      |
| CLI        |          |  (/connect/    |          |  Service       |
|            |          |   token)       |          |                |
+------------+          +----------------+          +----------------+
      ^                                                    |
      |                                                    |
      +----------------------------------------------------+
              { access_token, refresh_token, expires_in }
```

**Token Request:**
```http
POST /connect/token HTTP/1.1
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=dev-client
&client_secret=dev-secret
&scope=openid profile email servers:read servers:write nodes:manage
```

**Token Response:**
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "refresh_token": "rt_abc123...",
  "expires_in": 3600,
  "token_type": "Bearer"
}
```

### Token Storage

Tokens are stored in the configuration file:

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "refresh_token": "rt_abc123...",
  "token_expires_at": "2026-01-22T15:30:00Z"
}
```

### Token Validation

Before each authenticated request:

```csharp
public bool IsAuthenticated()
{
    return !string.IsNullOrWhiteSpace(AccessToken) &&
           TokenExpiresAt.HasValue &&
           TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(1);
}
```

- Tokens are considered expired 1 minute before actual expiration
- This provides buffer time for request completion

### Automatic Token Injection

The `AuthenticatedHttpClientHandler` automatically adds Bearer tokens:

```csharp
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    if (!string.IsNullOrEmpty(_accessToken))
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
    }
    return await base.SendAsync(request, cancellationToken);
}
```

---

## Output Formatting

The CLI uses Spectre.Console for rich terminal output.

### Tables

```csharp
var table = new Table()
    .Border(TableBorder.Rounded)
    .BorderColor(Color.Blue)
    .AddColumn("[bold]Name[/]")
    .AddColumn("[bold]Value[/]");

table.AddRow("Status", "[green]Healthy[/]");
AnsiConsole.Write(table);
```

### Panels

```csharp
var panel = new Panel(new Markup("[green]Success![/]"))
{
    Border = BoxBorder.Rounded,
    BorderStyle = new Style(Color.Green),
    Padding = new Padding(2, 1),
    Header = new PanelHeader(" Result ", Justify.Left)
};
AnsiConsole.Write(panel);
```

### Status Spinners

```csharp
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("blue"))
    .StartAsync("Loading...", async ctx =>
    {
        // Async work
        ctx.Status("Processing...");
    });
```

### Live Updates

```csharp
await AnsiConsole.Live(table)
    .StartAsync(async ctx =>
    {
        table.AddRow("Row 1", "Value 1");
        ctx.Refresh();

        await Task.Delay(100);

        table.AddRow("Row 2", "Value 2");
        ctx.Refresh();
    });
```

### Colors

Common color patterns:

| Color | Usage |
|-------|-------|
| `[green]` | Success, healthy status |
| `[red]` | Errors, failures |
| `[yellow]` | Warnings, attention needed |
| `[cyan]` | Commands, important values |
| `[dim]` | Secondary information, hints |
| `[bold]` | Headers, emphasis |

### Markup Escaping

Always escape user input:

```csharp
AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(userInput)}[/]");
```

---

## Error Handling

### Spectre.Console Errors (Interactive Commands)

For commands with rich UI output:

```csharp
catch (ApiException ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.StatusCode} - {Markup.Escape(ex.Message)}");
    return 1;
}
```

### JSON Errors (Identity Commands)

For scripting-friendly commands:

```csharp
catch (ApiException ex)
{
    return IdentityCommandHelpers.WriteApiError(ex);
}
```

Output:
```json
{
  "error": "http_error",
  "message": "404 NotFound",
  "details": {
    "error": "not_found",
    "message": "Organization not found"
  }
}
```

### Error Detail Sanitization

By default, error details are sanitized to only include safe fields:

- `error`
- `error_description`
- `error_uri`
- `message`
- `code`
- `correlation_id`
- `trace_id`
- `request_id`

Enable full error details with:

```bash
export DHADGAR_CLI_DEBUG=1
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error (authentication, API error, invalid input) |

Commands return meaningful exit codes for scripting:

```bash
if dhadgar auth status > /dev/null 2>&1; then
    echo "Authenticated"
else
    dhadgar auth login
fi
```

---

## Testing

### Test Project

Tests are in `tests/Dhadgar.Cli.Tests/`:

```
tests/Dhadgar.Cli.Tests/
├── Commands/
│   ├── CommandValidationTests.cs
│   └── Identity/
│       └── IdentityCommandHelpersTests.cs
├── Configuration/
│   └── CliConfigTests.cs
├── Utilities/
│   └── ExpirationParserTests.cs
├── HelloWorldTests.cs
└── Dhadgar.Cli.Tests.csproj
```

### Test Dependencies

| Package | Purpose |
|---------|---------|
| `xunit` | Test framework |
| `Microsoft.NET.Test.Sdk` | Test SDK |
| `xunit.runner.visualstudio` | VS integration |
| `NSubstitute` | Mocking |
| `FluentAssertions` | Fluent assertions |

### Running Tests

```bash
# Run all CLI tests
dotnet test tests/Dhadgar.Cli.Tests

# Run specific test
dotnet test tests/Dhadgar.Cli.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbosity
dotnet test tests/Dhadgar.Cli.Tests -v normal
```

### Internal Access

The CLI exposes internals to the test project:

```xml
<ItemGroup>
    <InternalsVisibleTo Include="Dhadgar.Cli.Tests" />
</ItemGroup>
```

This allows testing internal classes like `ExpirationParser` and `CommandValidation`.

---

## Development Guide

### Adding a New Command

1. **Create command class** in appropriate `Commands/` subdirectory:

```csharp
namespace Dhadgar.Cli.Commands.Example;

public sealed class MyCommand
{
    public static async Task<int> ExecuteAsync(
        string argument,
        string? optionalArg,
        CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (!config.IsAuthenticated())
        {
            AnsiConsole.MarkupLine("[red]Not authenticated.[/]");
            return 1;
        }

        using var factory = ApiClientFactory.TryCreate(config, out var error);
        if (factory is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        // Execute and display results
        return 0;
    }
}
```

2. **Register in Program.cs**:

```csharp
// Add using
using Dhadgar.Cli.Commands.Example;

// Create command with options
var exampleCmd = new Command("example", "Example command group");

var myCmd = new Command("my-command", "Does something useful");
var argArg = new Argument<string>("argument", "Required argument");
var optOpt = new Option<string?>("--optional", "Optional value");
myCmd.AddArgument(argArg);
myCmd.AddOption(optOpt);

myCmd.SetHandler(async (string arg, string? opt) =>
{
    await MyCommand.ExecuteAsync(arg, opt, CancellationToken.None);
}, argArg, optOpt);

exampleCmd.AddCommand(myCmd);
root.AddCommand(exampleCmd);
```

3. **Add API methods** if needed (in `Infrastructure/Clients/`):

```csharp
public interface IExampleApi
{
    [Get("/api/v1/example/{id}")]
    Task<ExampleResponse> GetAsync(string id, CancellationToken ct = default);
}
```

4. **Write tests** in `tests/Dhadgar.Cli.Tests/`:

```csharp
public class MyCommandTests
{
    [Fact]
    public void Should_validate_input()
    {
        // Test implementation
    }
}
```

### Coding Patterns

**Configuration Loading:**
```csharp
var config = CliConfig.Load();
```

**Authentication Check:**
```csharp
if (!config.IsAuthenticated())
{
    AnsiConsole.MarkupLine("[red]Not authenticated.[/]");
    return 1;
}
```

**API Client Creation:**
```csharp
using var factory = ApiClientFactory.TryCreate(config, out var error);
if (factory is null)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
    return 1;
}
```

**Status Spinner:**
```csharp
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("blue"))
    .StartAsync("Working...", async ctx => { ... });
```

**Error Handling:**
```csharp
try
{
    var result = await api.MethodAsync(ct);
}
catch (ApiException ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
    return 1;
}
```

---

## Troubleshooting

### "Not authenticated" Error

**Cause:** No valid token or token expired.

**Solution:**
```bash
dhadgar auth login
```

### "Invalid Identity URL" Error

**Cause:** Malformed URL in configuration.

**Solution:**
1. Check `~/.dhadgar/config.json`
2. Ensure URLs are absolute (include `http://` or `https://`)
3. Remove trailing slashes

### Connection Refused

**Cause:** Service not running.

**Solution:**
1. Start local infrastructure:
   ```bash
   docker compose -f deploy/compose/docker-compose.dev.yml up -d
   ```
2. Start the required services:
   ```bash
   dotnet run --project src/Dhadgar.Gateway
   dotnet run --project src/Dhadgar.Identity
   ```

### Token Expired Immediately

**Cause:** Clock skew between client and server.

**Solution:**
1. Sync system clock
2. Check `token_expires_at` in config vs actual time

### API Errors with No Details

**Cause:** Error details are sanitized by default.

**Solution:**
```bash
export DHADGAR_CLI_DEBUG=1
dhadgar <command>
```

### JSON Parse Errors

**Cause:** API returning unexpected format.

**Solution:**
1. Check service logs
2. Verify service version compatibility
3. Enable debug mode for full error details

### SSL/TLS Errors

**Cause:** Certificate validation issues.

**Solution:**
1. For local dev, services typically use HTTP
2. For production, ensure valid certificates
3. The CLI enables certificate revocation checking

---

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DHADGAR_CLI_DEBUG` | Enable full error details (`1` or `true`) | (disabled) |

---

## Security Considerations

### Token Storage

- Tokens are stored in `~/.dhadgar/config.json`
- File permissions should restrict access to the current user
- Add `~/.dhadgar/` to `.gitignore` if in a repository

### Secret Masking

- Secrets are masked by default (shown as `********`)
- Use `--reveal` flag explicitly to see values
- Revealed secrets are marked with `[red](REVEALED)[/]` header

### Certificate Revocation

- The CLI enables certificate revocation list checking:
  ```csharp
  CheckCertificateRevocationList = true
  ```

### Input Validation

- Vault names are validated with regex: `^[a-zA-Z0-9-]{3,24}$`
- Secret values are validated against Azure Key Vault size limits (25KB)
- User input is escaped before display: `Markup.Escape(input)`

### Sensitive Prompts

- Client secrets use hidden input: `new TextPrompt<string>().Secret()`
- Secret values prompt with hidden input when not provided as arguments

---

## Backend Implementation Status

**Working commands** (backend exists):

| Command | Status |
|---------|--------|
| `dhadgar auth login/status/logout` | Working |
| `dhadgar gateway health` | Working |
| `dhadgar identity orgs *` | Working |
| `dhadgar identity users *` | Working |
| `dhadgar identity roles *` | Working |
| `dhadgar me *` | Working |
| `dhadgar secret get` | Working |
| `dhadgar secret list` | Working |

**Planned commands** (CLI ready, backend needed):

| Command | Backend Requirement |
|---------|---------------------|
| `dhadgar secret set` | Secrets service write API |
| `dhadgar secret rotate` | Secret rotation logic |
| `dhadgar secret delete` | Secrets service delete API |
| `dhadgar secret list-certs` | Certificate SDK integration |
| `dhadgar secret import-cert` | Certificate SDK integration |
| `dhadgar keyvault *` | Azure Resource Manager SDK |
| `dhadgar gateway services` | Gateway diagnostics endpoint |
| `dhadgar gateway routes` | Gateway diagnostics endpoint |
| `dhadgar gateway clusters` | Gateway diagnostics endpoint |

See [SECRETS-SERVICE-IMPLEMENTATION-PLAN.md](../../../docs/SECRETS-SERVICE-IMPLEMENTATION-PLAN.md) for backend implementation details.

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [CLAUDE.md](../../../CLAUDE.md) | Repository-wide development guidance |
| [Dhadgar.Identity README](../Dhadgar.Identity/README.md) | Identity service documentation |
| [Dhadgar.Secrets README](../Dhadgar.Secrets/README.md) | Secrets service documentation |
| [Dhadgar.Gateway README](../Dhadgar.Gateway/README.md) | Gateway service documentation |
| [Docker Compose README](../../../deploy/compose/README.md) | Local development infrastructure |
| [SECRETS-SERVICE-IMPLEMENTATION-PLAN.md](../../../docs/SECRETS-SERVICE-IMPLEMENTATION-PLAN.md) | Backend implementation plan |

---

## License

Part of the Meridian Console (Dhadgar) project.
