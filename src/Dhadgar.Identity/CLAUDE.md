# Identity Service

User, organization, and role management. Core identity provider for the platform.

## Tech Stack
- ASP.NET Core Minimal API
- PostgreSQL with EF Core
- FluentValidation

## Port
5010

## Database
`dhadgar_identity` - Migrations in `Data/Migrations/`

## Key Entities
- Users, Organizations, Roles, Memberships
- OAuth provider accounts (Steam, Discord, etc.)
- Audit events

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults

## Notes
- Auto-applies migrations in Development mode
- Search endpoints support pagination and filtering
