# CLI Tool

Command-line interface for Meridian Console.

## Tech Stack

- .NET Console App
- System.CommandLine
- Spectre.Console for rich output
- Refit for typed HTTP clients

## Installation

Automatically installs as `dhadgar` global tool during build:

```bash
dotnet build src/Dhadgar.Cli -c Release
dhadgar --help
```

## Command Groups

| Command | Description |
| ------- | ----------- |
| `auth` | Authentication and token management |
| `me` | Self-service (profile, sessions, permissions) |
| `identity` | Organizations, users, and roles management |
| `member` | Organization member management |
| `secret` | Secret management (get, set, rotate) |
| `keyvault` | Azure Key Vault management |
| `gateway` | Gateway diagnostics and health |
| `nodes` | Node management (list, get, maintenance) |
| `enrollment` | Agent enrollment tokens |
| `commands` | List all available commands |
| `version` | Show CLI build info |

## Key Directories

- `Commands/` - Command implementations by service
- `Infrastructure/Clients/` - Refit API clients
- `Configuration/` - CLI config (stored in `~/.dhadgar/config.json`)

## Adding Commands

1. Create Refit interface in `Infrastructure/Clients/I{Service}Api.cs`
2. Update `ApiClientFactory.cs` with new client creation method
3. Create command files in `Commands/{Service}/`
4. Register commands in `Program.cs`

## Dependencies

- Dhadgar.Contracts - Shared DTOs
- Refit - Typed HTTP clients
- Spectre.Console - Rich terminal output
