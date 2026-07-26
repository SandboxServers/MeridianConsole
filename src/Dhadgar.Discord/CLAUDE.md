# Discord Service

Discord bot integration.

## Tech Stack
- ASP.NET Core
- Discord.NET

## Port
5120

## Status
Implemented (PR #39) — Discord.Net bot, slash commands, webhook delivery, EF migrations.

## Implemented Features
- Bot hosting (`DiscordBotService`) with health check reflecting connection state
- Slash command handling
- Notification delivery consumer (MassTransit)
- Platform health reporting across services
- Admin API (API-key protected): logs, platform health, channels

## Planned Features
- Server management commands
- User linking

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
