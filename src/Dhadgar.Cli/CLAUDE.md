# CLI Tool

Command-line interface for Meridian Console.

## Tech Stack
- .NET Console App
- System.CommandLine
- Spectre.Console for rich output

## Installation
Automatically installs as `dhadgar` global tool during build:
```bash
dotnet build src/Dhadgar.Cli -c Release
dhadgar --help
```

## Key Directories
- `Commands/` - Command implementations
- `Infrastructure/` - API clients, configuration

## Dependencies
- Dhadgar.Contracts
- Refit for typed HTTP clients
