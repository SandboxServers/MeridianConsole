# Mods Service

Mod registry and versioning.

## Tech Stack

- ASP.NET Core Minimal API
- PostgreSQL with EF Core

## Port

5080

## Status

Alpha - core functionality implemented.

## Implemented Features

- Mod CRUD with org-scoped multi-tenancy
- Semantic versioning with range parsing (npm-style: ^1.0.0, ~2.1.0, >=1.0.0 <2.0.0)
- Dependency resolution between mod versions
- Game compatibility tracking
- Category management
- Download tracking

## Dependencies

- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
