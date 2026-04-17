# Mods Service

Mod registry and versioning.

## Tech Stack

- ASP.NET Core Minimal API
- PostgreSQL with EF Core

## Port

5080

## Status

Alpha - core entities and versioning implemented; download/dependency services pending.

## Implemented Features

- Mod CRUD with org-scoped multi-tenancy (via `TenantScopedAuthorization`)
- Semantic versioning support with range parsing (`VersionRangeParser`: ^1.0.0, ~2.1.0, >=1.0.0 <2.0.0)
- Version range filtering for dependency queries
- Game compatibility tracking
- Database entities: `Mod`, `ModVersion`, `ModCategory`, `ModDependency`, `ModDownload`

## Not Yet Implemented

- **Download endpoint**: `ModDownload` entity exists but no download/tracking endpoint
- **Dependency resolution service**: Only `VersionRangeParser` filtering, no full resolution graph
- **Category management endpoints**: `ModCategory` entity exists but no CRUD endpoints
- **Files service integration**: Required for actual mod file distribution

## Dependencies

- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
