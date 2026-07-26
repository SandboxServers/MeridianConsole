# Notifications Service

Email, Discord, and webhook notifications.

## Tech Stack
- ASP.NET Core Minimal API
- PostgreSQL with EF Core

## Port
5090

## Status
Implemented (PR #39) — email dispatch, alerting, MassTransit consumers, EF migrations.

## Implemented Features
- Office 365 / SMTP email delivery
- Alert dispatching with throttling
- MassTransit consumers (ServerStarted/Stopped/Crashed, SendEmail; Node* consumers exist but are not yet registered in Program.cs)
- Notification log persistence with EF outbox
- Admin API (API-key protected): logs query, test send

## Planned Features
- Template management
- Delivery tracking
- Register the Node offline/degraded/capacity consumers (waiting on Nodes-side integration)

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
- Dhadgar.Messaging
