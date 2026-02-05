# Servers Service

Game server lifecycle management.

## Tech Stack
- ASP.NET Core Minimal API
- PostgreSQL with EF Core

## Port
5030

## Status
Alpha - core functionality implemented.

## Implemented Features
- Server CRUD with org-scoped multi-tenancy
- 13-state lifecycle state machine (Pending, Installing, Stopped, Starting, Running, etc.)
- Server templates for game-specific defaults
- Port allocation and configuration management
- Power state tracking (On, Off, Suspended)
- FluentValidation for request validation

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
