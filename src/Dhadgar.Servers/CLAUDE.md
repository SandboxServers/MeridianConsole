# Servers Service

Game server lifecycle management.

## Tech Stack
- ASP.NET Core Minimal API
- PostgreSQL with EF Core

## Port
5030

## Status
Stub - real implementation open in PR #88 (lifecycle state machine, templates, CRUD + lifecycle endpoints).

## Planned Features
- Server configuration and templates
- Start/stop/restart operations
- Status tracking

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
