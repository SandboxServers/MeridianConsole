# Console Service

Real-time server console streaming.

## Tech Stack
- ASP.NET Core
- SignalR for real-time communication
- PostgreSQL with EF Core
- Redis for session management

## Port
5070

## Status
Alpha - core functionality implemented.

## Implemented Features
- SignalR hub for real-time bidirectional communication
- Console session management with Redis
- Command dispatch to agents
- Console history with hot/cold storage pattern
- Command audit logging
- REST endpoints for history management

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
