# Dhadgar CLI

Beautiful command-line interface for **Meridian Console** — the modern game server control plane.

Built with [Spectre.Console](https://spectreconsole.net/) for gorgeous terminal UI with colors, tables, spinners, and interactive prompts.

## Features

✨ **Beautiful UI** - Rich terminal output with colors, tables, and status indicators
🔐 **Authentication** - OAuth client credentials flow with token management
🏢 **Organization Management** - Create, list, switch between organizations
👥 **Member Management** - View members, roles, and permissions
🔑 **Secret Management** - Secure access to OAuth, BetterAuth, and infrastructure secrets
💚 **Health Monitoring** - Real-time service health checks with response times

## Installation

```bash
# Build the CLI
dotnet build src/Dhadgar.Cli

# Run directly
dotnet run --project src/Dhadgar.Cli

# Or publish as a standalone executable
dotnet publish src/Dhadgar.Cli -c Release -o ./publish
```

## Configuration

Configuration is stored in `~/.dhadgar/config.json` and managed automatically by the CLI.

Default service URLs:
- Gateway: `http://localhost:5000`
- Identity: `http://localhost:5001`
- Secrets: `http://localhost:5002`

## Commands

### Authentication

```bash
# Interactive login with OAuth client credentials
dhadgar auth login

# Login with explicit credentials
dhadgar auth login --client-id dev-client --client-secret dev-secret

# Show authentication status and configuration
dhadgar auth status
```

### Organization Management

```bash
# List all organizations you're a member of
dhadgar org list

# Create a new organization (interactive prompt)
dhadgar org create

# Create with explicit name
dhadgar org create "My Organization"

# Switch to a different organization (updates tokens)
dhadgar org switch <org-id>
```

### Member Management

```bash
# List members of current organization
dhadgar member list

# List members of specific organization
dhadgar member list <org-id>
```

### Secret Management

```bash
# Get a single secret (masked by default)
dhadgar secret get <secret-name>

# Reveal the actual secret value
dhadgar secret get <secret-name> --reveal

# Set or update a secret value
dhadgar secret set <secret-name> <value>

# Set secret with interactive prompt
dhadgar secret set <secret-name>

# Set secret from stdin (useful for piping)
echo "my-secret-value" | dhadgar secret set <secret-name> --stdin

# Rotate a secret (generate new value, invalidate old)
dhadgar secret rotate <secret-name>

# Force rotation without confirmation
dhadgar secret rotate <secret-name> --force

# List OAuth provider secrets
dhadgar secret list oauth

# List BetterAuth secrets
dhadgar secret list betterauth

# List infrastructure secrets (database, messaging)
dhadgar secret list infrastructure

# Reveal all secrets in a category
dhadgar secret list oauth --reveal

# List all certificates
dhadgar secret list-certs

# List certificates in a specific Key Vault
dhadgar secret list-certs --vault my-vault

# Import a certificate
dhadgar secret import-cert /path/to/cert.pfx

# Import with custom name and password
dhadgar secret import-cert /path/to/cert.pfx --name my-cert --password secret123

# Import to specific vault
dhadgar secret import-cert /path/to/cert.pfx --vault my-vault
```

### Azure Key Vault Management

```bash
# List all Key Vaults
dhadgar keyvault list

# Get detailed vault information
dhadgar keyvault get my-vault

# Create a new Key Vault (interactive)
dhadgar keyvault create

# Create with explicit parameters
dhadgar keyvault create my-vault --location centralus

# Update vault properties
dhadgar keyvault update my-vault --enable-soft-delete
dhadgar keyvault update my-vault --enable-purge-protection
dhadgar keyvault update my-vault --retention-days 90
dhadgar keyvault update my-vault --sku premium
```

## Backend Implementation Status

⚠️ **Note:** The CLI commands are fully implemented, but the backend API endpoints are **not yet implemented**.

See [SECRETS-SERVICE-IMPLEMENTATION-PLAN.md](../../../docs/SECRETS-SERVICE-IMPLEMENTATION-PLAN.md) for details on what needs to be built in the Secrets service.

**Working commands** (backend exists):
- `dhadgar secret get` - Get secret values
- `dhadgar secret list` - List secrets by category

**Planned commands** (CLI ready, backend needed):
- `dhadgar secret set` - Update secrets (requires backend write API)
- `dhadgar secret rotate` - Rotate secrets (requires rotation logic)
- `dhadgar secret list-certs` - List certificates (requires Certificate SDK)
- `dhadgar secret import-cert` - Import certificates (requires Certificate SDK)
- `dhadgar keyvault *` - All vault management (requires ResourceManager SDK)

### Gateway Diagnostics

```bash
# Check health of all services
dhadgar gateway health

# Legacy health check command
dhadgar ping --url http://localhost:5000/healthz
```

## Beautiful Output Examples

### Auth Status
```
╭────────────────┬───────────────────────╮
│ Setting        │ Value                 │
├────────────────┼───────────────────────┤
│ Gateway URL    │ http://localhost:5000 │
│ Identity URL   │ http://localhost:5001 │
│ Secrets URL    │ http://localhost:5002 │
│ Current Org ID │ none                  │
│ Authentication │ ⚠ Not authenticated   │
│ Token Expires  │ n/a                   │
╰────────────────┴───────────────────────╯
```

### Organization List
```
╭─────────────┬──────────────────┬────────┬────────╮
│ ID          │ Name             │ Role   │ Status │
├─────────────┼──────────────────┼────────┼────────┤
│ 3a5e7c...   │ My Org ← current │ owner  │ ●      │
│ 8f2b9d...   │ Test Org         │ admin  │ ●      │
╰─────────────┴──────────────────┴────────┴────────╯
```

### Health Check
```
╭──────────┬───────────┬───────────────┬─────────╮
│ Service  │ Status    │ Response Time │ Message │
├──────────┼───────────┼───────────────┼─────────┤
│ Gateway  │ ✓ Healthy │ 53ms          │ ok      │
│ Identity │ ✓ Healthy │ 127ms         │ ok      │
│ Secrets  │ ✓ Healthy │ 94ms          │ ok      │
╰──────────┴───────────┴───────────────┴─────────╯
```

### Secret Management
```
╔═══════════════════════════════════════════════════╗
║  OAUTH Secrets (Masked)                           ║
╠═══════════════════════════════════════════════════╣
║ ╭─────────────────────────┬─────────────────────╮ ║
║ │ Secret Name             │ Value               │ ║
║ ├─────────────────────────┼─────────────────────┤ ║
║ │ Steam-ClientId          │ ••••••••••••••••    │ ║
║ │ Steam-ClientSecret      │ ••••••••••••••••    │ ║
║ │ BattleNet-ClientId      │ ••••••••••••••••    │ ║
║ │ BattleNet-ClientSecret  │ ••••••••••••••••    │ ║
║ ╰─────────────────────────┴─────────────────────╯ ║
╚═══════════════════════════════════════════════════╝

Use --reveal to show actual values
```

## Security

- Tokens are stored in `~/.dhadgar/config.json` with restricted file permissions
- Secrets are masked by default and require `--reveal` flag to display
- All API communication uses bearer token authentication
- Configuration file should be added to `.gitignore` if in a repository

## Development

Built with:
- **.NET 10.0** - Modern C# with latest features
- **System.CommandLine** - Powerful CLI framework
- **Spectre.Console** - Beautiful terminal UI library
- **System.Net.Http.Json** - JSON API communication

Project structure:
```
Dhadgar.Cli/
├── Commands/           # Command implementations
│   ├── Auth/          # Authentication commands
│   ├── Gateway/       # Gateway diagnostics
│   ├── Member/        # Member management
│   ├── Org/           # Organization management
│   └── Secret/        # Secret management
├── Configuration/     # Config file management
├── Infrastructure/    # HTTP client with auth
└── Program.cs         # Command wiring
```

## Contributing

When adding new commands:
1. Create command class in appropriate `Commands/` subdirectory
2. Use `Spectre.Console` for all output (tables, panels, status, colors)
3. Wire up in `Program.cs` using `System.CommandLine`
4. Follow existing patterns for consistency
5. Update this README with new command documentation

## License

Part of the Meridian Console (Dhadgar) project.
