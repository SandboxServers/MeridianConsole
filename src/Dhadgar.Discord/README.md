# Dhadgar.Discord Service

Discord bot integration service for Meridian Console, providing Discord-based server management, notifications, and user interaction capabilities.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Technology Stack](#technology-stack)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [API Endpoints](#api-endpoints)
- [Planned Features](#planned-features)
- [Discord Bot Setup](#discord-bot-setup)
- [Integration Points](#integration-points)
- [Architecture](#architecture)
- [Testing](#testing)
- [Docker](#docker)
- [Troubleshooting](#troubleshooting)

---

## Overview

The Dhadgar.Discord service is designed to provide comprehensive Discord integration for the Meridian Console platform. It will enable game server administrators to manage their servers directly from Discord using slash commands, receive real-time notifications about server events, and link their Discord accounts for seamless authentication.

### Key Responsibilities

- **Discord Bot Operations**: Hosts and manages the Discord bot that interacts with users
- **Slash Command Handling**: Processes Discord slash commands for server management
- **Status Notifications**: Sends server status updates to configured Discord channels
- **User Account Linking**: Associates Discord accounts with Meridian Console user profiles
- **Webhook Integration**: Receives and processes Discord webhook events
- **Rich Embed Messages**: Generates formatted status embeds with player counts and server information

### What This Service Is NOT

- This is NOT a game server hosting service - it orchestrates servers on customer-owned hardware
- This service does NOT store game data - it only manages Discord-related metadata and configurations
- Bot commands do NOT bypass normal authorization - they integrate with the Identity service

---

## Current Status

**Status: STUB - Basic scaffolding in place, core functionality planned**

### What Currently Exists

| Component | Status | Description |
|-----------|--------|-------------|
| Service scaffolding | Complete | ASP.NET Core Minimal API project structure |
| Health endpoints | Complete | `/healthz`, `/livez`, `/readyz` endpoints |
| Swagger/OpenAPI | Complete | API documentation in Development mode |
| OpenTelemetry | Complete | Distributed tracing and metrics instrumentation |
| Standard middleware | Complete | Correlation tracking, problem details, request logging |
| Gateway routing | Complete | Route configured at `/api/v1/discord/{**catch-all}` |
| Docker support | Complete | Production-ready Dockerfile with health checks |
| Test project | Complete | Basic unit and integration test scaffolding |

### Current Endpoints

```
GET /              - Service info banner
GET /hello         - Hello world smoke test
GET /healthz       - Full health check (all checks)
GET /livez         - Liveness probe (self check only)
GET /readyz        - Readiness probe (dependency checks)
```

### What Is Planned (Not Yet Implemented)

- Discord.NET integration and bot client
- Slash command registration and handling
- Server status notification system
- Player count/status embed generation
- Discord account linking with OAuth2
- Guild (server) configuration management
- Channel permission management
- Role-based command access control
- Webhook event processing
- MassTransit message consumers for server events

---

## Technology Stack

### Current Dependencies

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime and SDK |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| MassTransit | 8.3.6 | Message bus abstraction |
| MassTransit.RabbitMQ | 8.3.6 | RabbitMQ transport |
| OpenTelemetry | 1.14.0 | Distributed tracing and metrics |
| Swashbuckle | 10.1.0 | OpenAPI/Swagger documentation |

### Planned Dependencies

| Technology | Purpose |
|------------|---------|
| Discord.NET | Official Discord API wrapper for .NET |
| Entity Framework Core | Database access for guild configurations |
| PostgreSQL (Npgsql) | Persistent storage |

### Internal Dependencies

| Project | Purpose |
|---------|---------|
| Dhadgar.Contracts | Shared DTOs and message contracts |
| Dhadgar.Shared | Common utilities and primitives |
| Dhadgar.Messaging | MassTransit conventions and configuration |
| Dhadgar.ServiceDefaults | Standard middleware, health checks, observability |

---

## Quick Start

### Prerequisites

1. **.NET 10 SDK** - Pinned in `global.json`
2. **Docker** (optional) - For local infrastructure
3. **Discord Bot Token** (for full functionality) - See [Discord Bot Setup](#discord-bot-setup)

### Running Locally

#### Option 1: Direct Execution

```bash
# From solution root
dotnet run --project src/Dhadgar.Discord
```

The service will start on `http://localhost:5012`.

#### Option 2: With Hot Reload

```bash
dotnet watch --project src/Dhadgar.Discord
```

#### Option 3: With Local Infrastructure

Start the local development infrastructure first:

```bash
# Start PostgreSQL, RabbitMQ, Redis, and observability stack
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Then run the service:

```bash
dotnet run --project src/Dhadgar.Discord
```

### Verify Service is Running

```bash
# Service info
curl http://localhost:5012/

# Health check
curl http://localhost:5012/healthz

# Hello world
curl http://localhost:5012/hello
```

Expected responses:

```json
// GET /
{"service":"Dhadgar.Discord","message":"Hello from Dhadgar.Discord"}

// GET /healthz
{"service":"Dhadgar.Discord","status":"ok","timestamp":"2026-01-22T...","checks":{"self":{"status":"Healthy","duration_ms":0.1}}}

// GET /hello
Hello from Dhadgar.Discord
```

### Accessing Swagger UI

In Development mode, Swagger UI is available at:

```
http://localhost:5012/swagger
```

---

## Configuration

### Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Base configuration |
| `appsettings.Development.json` | Development overrides (create if needed) |
| User Secrets | Sensitive values (bot tokens, etc.) |

### Current Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar",
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

### Planned Configuration Structure

```json
{
  "Discord": {
    "BotToken": "Use user-secrets for this!",
    "ApplicationId": "your-application-id",
    "PublicKey": "your-public-key",
    "Intents": [
      "Guilds",
      "GuildMessages",
      "GuildMembers"
    ],
    "StatusUpdateIntervalSeconds": 60,
    "MaxRetryAttempts": 3
  },
  "Commands": {
    "GlobalCommandPrefix": "/meridian",
    "EnableDMCommands": false,
    "RateLimitPerUser": 5,
    "RateLimitWindowSeconds": 60
  },
  "Notifications": {
    "DefaultEmbedColor": "#5865F2",
    "MaxEmbedsPerMessage": 10,
    "EnableMentions": true
  },
  "Linking": {
    "OAuth2RedirectUri": "https://api.meridianconsole.com/api/v1/discord/oauth/callback",
    "LinkTokenExpirationMinutes": 15,
    "RequireEmailVerification": true
  }
}
```

### User Secrets (Recommended for Local Development)

```bash
# Initialize user secrets for the project
dotnet user-secrets init --project src/Dhadgar.Discord

# Set the Discord bot token
dotnet user-secrets set "Discord:BotToken" "your-bot-token-here" --project src/Dhadgar.Discord

# Set the application ID
dotnet user-secrets set "Discord:ApplicationId" "your-application-id" --project src/Dhadgar.Discord

# Set OAuth2 client secret (for account linking)
dotnet user-secrets set "Discord:ClientSecret" "your-oauth-client-secret" --project src/Dhadgar.Discord

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Discord
```

### Environment Variables

Configuration can also be set via environment variables using the standard ASP.NET Core pattern:

```bash
# Double underscore for nested properties
export Discord__BotToken="your-bot-token"
export Discord__ApplicationId="your-app-id"
export ConnectionStrings__Postgres="Host=localhost;Database=dhadgar_discord"
```

### OpenTelemetry Configuration (Optional)

To enable telemetry export to the local OTLP collector:

```bash
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Discord
```

This enables traces, metrics, and logs to flow to Prometheus/Loki/Grafana.

---

## API Endpoints

### Current Endpoints

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| GET | `/` | Service info banner | No |
| GET | `/hello` | Hello world smoke test | No |
| GET | `/healthz` | Full health check | No |
| GET | `/livez` | Liveness probe | No |
| GET | `/readyz` | Readiness probe | No |

### Planned Endpoints

#### Guild Management

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| GET | `/guilds` | List linked Discord guilds | Yes (Org Admin) |
| POST | `/guilds/{guildId}/link` | Link a Discord guild to organization | Yes (Org Admin) |
| DELETE | `/guilds/{guildId}/unlink` | Unlink a Discord guild | Yes (Org Admin) |
| GET | `/guilds/{guildId}` | Get guild configuration | Yes (Org Member) |
| PUT | `/guilds/{guildId}` | Update guild configuration | Yes (Org Admin) |

#### Channel Configuration

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| GET | `/guilds/{guildId}/channels` | List notification channels | Yes (Org Member) |
| POST | `/guilds/{guildId}/channels` | Add notification channel | Yes (Org Admin) |
| PUT | `/guilds/{guildId}/channels/{channelId}` | Update channel config | Yes (Org Admin) |
| DELETE | `/guilds/{guildId}/channels/{channelId}` | Remove notification channel | Yes (Org Admin) |

#### Account Linking

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| GET | `/oauth/authorize` | Initiate Discord OAuth2 flow | Yes (User) |
| GET | `/oauth/callback` | OAuth2 callback handler | No (OAuth flow) |
| POST | `/link/verify` | Verify link via DM code | Yes (User) |
| DELETE | `/link` | Unlink Discord account | Yes (User) |
| GET | `/link/status` | Check link status | Yes (User) |

#### Command Management (Admin)

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| POST | `/commands/sync` | Sync slash commands with Discord | Yes (System Admin) |
| GET | `/commands` | List registered commands | Yes (System Admin) |
| GET | `/commands/stats` | Command usage statistics | Yes (System Admin) |

#### Webhook (Discord Interactions)

| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| POST | `/webhook/interactions` | Discord interactions endpoint | Signature verification |

### Gateway Routing

All Discord service endpoints are accessible through the Gateway at:

```
https://api.meridianconsole.com/api/v1/discord/{path}
```

Route configuration (from Gateway `appsettings.json`):

```json
{
  "discord-route": {
    "ClusterId": "discord",
    "Order": 20,
    "Match": { "Path": "/api/v1/discord/{**catch-all}" },
    "AuthorizationPolicy": "TenantScoped",
    "RateLimiterPolicy": "PerTenant",
    "Transforms": [
      { "PathRemovePrefix": "/api/v1/discord" }
    ]
  }
}
```

---

## Planned Features

### 1. Discord Bot Core

The service will host a Discord.NET-based bot that maintains a persistent connection to Discord's gateway.

**Planned Capabilities:**
- Automatic reconnection on connection loss
- Gateway intent optimization (only subscribe to needed events)
- Sharding support for large deployments (future)
- Activity status showing service health

**Bot Status Display:**
```
Playing: Monitoring 42 servers | /help
```

### 2. Slash Commands

Interactive commands for server management directly from Discord.

#### Server Management Commands

| Command | Description | Required Permission |
|---------|-------------|---------------------|
| `/server list` | List your game servers | `servers:read` |
| `/server status <name>` | Get detailed server status | `servers:read` |
| `/server start <name>` | Start a stopped server | `servers:start` |
| `/server stop <name>` | Stop a running server | `servers:stop` |
| `/server restart <name>` | Restart a server | `servers:restart` |
| `/server players <name>` | List online players | `servers:read` |

#### Node Management Commands

| Command | Description | Required Permission |
|---------|-------------|---------------------|
| `/node list` | List organization nodes | `nodes:read` |
| `/node status <name>` | Get node health status | `nodes:read` |

#### Configuration Commands

| Command | Description | Required Permission |
|---------|-------------|---------------------|
| `/config notifications` | Configure notification channel | `org:admin` |
| `/config commands` | Configure command permissions | `org:admin` |
| `/config link` | Link Discord account | User must be authenticated |
| `/help` | Show available commands | None |

#### Command Response Example

```
/server status my-minecraft-server
```

**Discord Embed Response:**
```
+------------------------------------------+
| MY-MINECRAFT-SERVER                       |
| Status: Online                            |
+------------------------------------------+
| Players: 12/100                           |
| Uptime: 3d 14h 22m                        |
| CPU: 45%  |  Memory: 2.4/8 GB             |
| Node: us-west-node-01                     |
+------------------------------------------+
| [Start] [Stop] [Restart] [Console]        |
+------------------------------------------+
| Last updated: 2 minutes ago               |
+------------------------------------------+
```

### 3. Status Notifications

Automatic notifications to configured Discord channels for server events.

**Supported Events:**

| Event | Notification Content |
|-------|---------------------|
| Server Started | Server name, node, connection info |
| Server Stopped | Server name, stop reason, duration |
| Server Crashed | Server name, crash details, restart status |
| Player Joined | Player name, server, player count |
| Player Left | Player name, server, player count |
| High CPU Alert | Server name, CPU %, duration |
| High Memory Alert | Server name, memory usage |
| Disk Space Warning | Node name, available space |
| Node Offline | Node name, last seen, affected servers |

**Notification Configuration Options:**
- Channel selection per notification type
- Mention roles for critical alerts
- Quiet hours (suppress non-critical notifications)
- Rate limiting to prevent notification spam

### 4. Rich Embed Messages

Formatted Discord embeds for server status and information.

**Server Status Embed Components:**
- Server name and game type icon
- Online/offline status indicator
- Player count with progress bar
- Resource utilization metrics
- Quick action buttons (Start/Stop/Restart)
- Connection information (when applicable)
- Timestamp of last update

**Color Coding:**
- Green: Server online, healthy
- Yellow: Server online, warnings present
- Red: Server offline or critical issues
- Gray: Server stopped (intentional)

### 5. Role-Based Command Access

Integration with Discord roles for permission management.

**Permission Mapping:**
```
Discord Role          -> Meridian Permission
Server Owner          -> org:admin, servers:*, nodes:*
Server Moderator      -> servers:read, servers:start, servers:stop
Server Member         -> servers:read
@everyone            -> (no access)
```

**Configuration via Web Panel:**
- Map Discord roles to Meridian permissions
- Override permissions per command
- Require multi-factor confirmation for destructive actions

### 6. Account Linking

Connect Discord accounts to Meridian Console user profiles.

**Linking Flow:**
1. User initiates link from Discord (`/config link`) or web panel
2. Service generates a time-limited verification code
3. User completes verification via the other platform
4. Accounts are associated in the Identity service
5. Future commands use linked account for authorization

**Benefits of Linked Accounts:**
- Commands use existing Meridian permissions
- SSO capability (login via Discord)
- Unified notification preferences
- Profile synchronization (avatar, username)

### 7. Webhook Integration

Process Discord interactions for stateless command handling.

**Supported Webhook Events:**
- Application commands (slash commands)
- Message components (button clicks)
- Autocomplete requests
- Modal submissions

**Webhook Security:**
- Ed25519 signature verification
- Timestamp validation (anti-replay)
- Rate limiting per guild

---

## Discord Bot Setup

### Step 1: Create Discord Application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Click "New Application"
3. Name it (e.g., "Meridian Console")
4. Note the **Application ID** and **Public Key**

### Step 2: Configure Bot

1. Navigate to the "Bot" section
2. Click "Add Bot"
3. Configure bot settings:
   - **Username**: Your preferred bot name
   - **Icon**: Upload a bot avatar
   - **Public Bot**: Disable if you want to control where it's added
   - **Require OAuth2 Code Grant**: Disable
4. Under "Privileged Gateway Intents":
   - Enable **Server Members Intent** (for member tracking)
   - Enable **Message Content Intent** (if using prefix commands)
5. Copy the **Bot Token** (keep this secret!)

### Step 3: Generate Invite URL

1. Navigate to the "OAuth2" -> "URL Generator" section
2. Select scopes:
   - `bot`
   - `applications.commands`
3. Select bot permissions:
   - Send Messages
   - Send Messages in Threads
   - Embed Links
   - Attach Files
   - Read Message History
   - Use External Emojis
   - Add Reactions
   - Use Slash Commands
4. Copy the generated URL

**Recommended Permission Integer:** `277025704000`

### Step 4: Configure OAuth2 (For Account Linking)

1. Navigate to "OAuth2" -> "General"
2. Add redirect URL:
   - Development: `http://localhost:5012/oauth/callback`
   - Production: `https://api.meridianconsole.com/api/v1/discord/oauth/callback`
3. Note the **Client Secret**

### Step 5: Set Up Interactions Endpoint (For Webhook Mode)

1. Navigate to "General Information"
2. Set **Interactions Endpoint URL**:
   - Production: `https://api.meridianconsole.com/api/v1/discord/webhook/interactions`
3. Discord will verify the endpoint with a signature check

### Step 6: Configure the Service

```bash
# Set required secrets
dotnet user-secrets set "Discord:BotToken" "YOUR_BOT_TOKEN" --project src/Dhadgar.Discord
dotnet user-secrets set "Discord:ApplicationId" "YOUR_APPLICATION_ID" --project src/Dhadgar.Discord
dotnet user-secrets set "Discord:PublicKey" "YOUR_PUBLIC_KEY" --project src/Dhadgar.Discord
dotnet user-secrets set "Discord:ClientSecret" "YOUR_OAUTH_CLIENT_SECRET" --project src/Dhadgar.Discord
```

### Step 7: Register Slash Commands

After the bot is invited and configured, sync commands:

```bash
# Via API (when implemented)
curl -X POST https://api.meridianconsole.com/api/v1/discord/commands/sync \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Or commands will auto-register on bot startup (planned).

---

## Integration Points

### Inter-Service Communication

The Discord service integrates with other Meridian Console services via HTTP APIs and MassTransit messaging.

```
+----------------+         +----------------+
|   Dhadgar      |  HTTP   |   Dhadgar      |
|   Discord      |<------->|   Identity     |
+----------------+         +----------------+
        |
        | HTTP
        v
+----------------+         +----------------+
|   Dhadgar      |  HTTP   |   Dhadgar      |
|   Servers      |<------->|   Nodes        |
+----------------+         +----------------+
        |
        | MassTransit
        v
+----------------+
|   Dhadgar      |
|  Notifications |
+----------------+
```

### Identity Service Integration

**Purpose:** User authentication and authorization

**HTTP Endpoints Used:**
- `GET /api/v1/identity/users/{userId}` - Get user profile
- `POST /api/v1/identity/oauth/discord/link` - Link Discord account
- `DELETE /api/v1/identity/oauth/discord` - Unlink Discord account
- `GET /api/v1/identity/permissions/{userId}` - Get user permissions

**Events Consumed (MassTransit):**
- `UserDeactivated` - Remove Discord links for deactivated users
- `OAuthAccountLinked` - Sync linked Discord accounts
- `OAuthAccountUnlinked` - Handle unlink events

### Servers Service Integration

**Purpose:** Game server status and control

**HTTP Endpoints Used:**
- `GET /api/v1/servers` - List servers
- `GET /api/v1/servers/{id}` - Get server details
- `POST /api/v1/servers/{id}/start` - Start server
- `POST /api/v1/servers/{id}/stop` - Stop server
- `POST /api/v1/servers/{id}/restart` - Restart server

**Events Consumed (MassTransit):**
- `ServerProvisioned` - New server available notification
- `ServerStatusChanged` - Server state change notification (planned)
- `ServerMetricsUpdated` - Real-time metrics for status embeds (planned)

### Nodes Service Integration

**Purpose:** Node health and availability

**HTTP Endpoints Used:**
- `GET /api/v1/nodes` - List nodes
- `GET /api/v1/nodes/{id}` - Get node details

**Events Consumed (MassTransit):**
- `NodeOffline` - Alert when node goes offline (planned)
- `NodeHealthChanged` - Node health status changes (planned)

### Notifications Service Integration

**Purpose:** Multi-channel notification orchestration

**Events Published (MassTransit):**
- `DiscordNotificationRequested` - Request notification delivery (planned)

**Events Consumed (MassTransit):**
- `NotificationDeliveryRequested` - Handle Discord delivery channel (planned)

### Message Contracts

Located in `Dhadgar.Contracts`, the following contracts are relevant:

```csharp
// Servers/Contracts.cs
public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);

public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
```

**Planned Discord-Specific Contracts:**

```csharp
// Discord/Contracts.cs (planned)
public record DiscordGuildLinked(
    Guid OrganizationId,
    ulong GuildId,
    string GuildName,
    Guid LinkedByUserId,
    DateTimeOffset OccurredAtUtc);

public record DiscordNotificationRequested(
    ulong GuildId,
    ulong ChannelId,
    string NotificationType,
    IReadOnlyDictionary<string, object> Payload,
    DateTimeOffset OccurredAtUtc);

public record DiscordAccountLinked(
    Guid UserId,
    ulong DiscordUserId,
    string DiscordUsername,
    DateTimeOffset OccurredAtUtc);
```

---

## Architecture

### Project Structure

```
src/Dhadgar.Discord/
+-- Program.cs                     # Application entry point, service configuration
+-- Hello.cs                       # Smoke test message constant
+-- appsettings.json               # Base configuration
+-- Properties/
|   +-- launchSettings.json        # Development launch profiles
+-- Dockerfile                     # Multi-stage Docker build
+-- Dockerfile.pipeline            # CI/CD artifact-based Docker build
+-- Dhadgar.Discord.csproj         # Project file
|
+-- (Planned) Bot/
|   +-- DiscordBotService.cs       # Hosted service for bot lifecycle
|   +-- DiscordClientFactory.cs    # Creates and configures DiscordSocketClient
|
+-- (Planned) Commands/
|   +-- ServerCommands.cs          # /server slash command handlers
|   +-- NodeCommands.cs            # /node slash command handlers
|   +-- ConfigCommands.cs          # /config slash command handlers
|   +-- HelpCommand.cs             # /help command handler
|
+-- (Planned) Handlers/
|   +-- InteractionHandler.cs      # Routes interactions to commands
|   +-- ButtonHandler.cs           # Handles button component interactions
|   +-- ModalHandler.cs            # Handles modal submissions
|
+-- (Planned) Services/
|   +-- GuildConfigurationService.cs    # Guild settings management
|   +-- NotificationService.cs          # Sends notifications to channels
|   +-- AccountLinkingService.cs        # Discord<->Meridian account linking
|   +-- EmbedBuilderService.cs          # Builds rich embed messages
|
+-- (Planned) Consumers/
|   +-- ServerEventConsumer.cs     # MassTransit consumer for server events
|   +-- NodeEventConsumer.cs       # MassTransit consumer for node events
|   +-- UserEventConsumer.cs       # MassTransit consumer for user events
|
+-- (Planned) Data/
|   +-- DiscordDbContext.cs        # EF Core database context
|   +-- Entities/
|   |   +-- GuildConfiguration.cs  # Linked guild settings
|   |   +-- ChannelConfiguration.cs # Notification channel settings
|   |   +-- DiscordLink.cs         # User account links
|   +-- Migrations/                # EF Core migrations
```

### Service Lifecycle

```
Application Start
       |
       v
+-------------------+
| Configure Services|
| - Discord client  |
| - MassTransit     |
| - DbContext       |
+-------------------+
       |
       v
+-------------------+
| Start Bot Service |
| - Connect gateway |
| - Register cmds   |
| - Start listening |
+-------------------+
       |
       v
+-------------------+
| Process Events    |<--+
| - Commands        |   |
| - Messages        |   | (Event Loop)
| - Interactions    |   |
+-------------------+---+
       |
       v (Shutdown)
+-------------------+
| Graceful Shutdown |
| - Stop consumers  |
| - Disconnect bot  |
| - Flush telemetry |
+-------------------+
```

### Data Flow for Slash Commands

```
User types /server status my-server
              |
              v
+-------------------------+
|  Discord Gateway        |
|  (Interaction Created)  |
+-------------------------+
              |
              v
+-------------------------+
|  Dhadgar.Discord        |
|  InteractionHandler     |
+-------------------------+
              |
              | 1. Parse command
              | 2. Validate permissions
              v
+-------------------------+
|  Identity Service       |
|  GET /permissions/{id}  |
+-------------------------+
              |
              | 3. Get server data
              v
+-------------------------+
|  Servers Service        |
|  GET /servers/{name}    |
+-------------------------+
              |
              | 4. Build embed
              v
+-------------------------+
|  EmbedBuilderService    |
|  Create status embed    |
+-------------------------+
              |
              | 5. Respond
              v
+-------------------------+
|  Discord Gateway        |
|  (Interaction Response) |
+-------------------------+
              |
              v
User sees embed response
```

### Middleware Pipeline

The service uses standard Dhadgar middleware configured in `Program.cs`:

```csharp
// Standard middleware (from ServiceDefaults)
app.UseMiddleware<CorrelationMiddleware>();   // Adds X-Correlation-ID, X-Request-ID
app.UseMiddleware<ProblemDetailsMiddleware>(); // RFC 7807 error responses
app.UseMiddleware<RequestLoggingMiddleware>(); // Request/response logging
```

---

## Testing

### Test Project

Tests are located in `tests/Dhadgar.Discord.Tests/`.

### Current Tests

| Test Class | Description |
|------------|-------------|
| `HelloWorldTests` | Verifies the Hello message constant |
| `SwaggerTests` | Verifies Swagger UI and OpenAPI spec generation |

### Running Tests

```bash
# Run all Discord service tests
dotnet test tests/Dhadgar.Discord.Tests

# Run with verbose output
dotnet test tests/Dhadgar.Discord.Tests --verbosity normal

# Run specific test
dotnet test tests/Dhadgar.Discord.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with coverage
dotnet test tests/Dhadgar.Discord.Tests --collect:"XPlat Code Coverage"
```

### Test Project Dependencies

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\..\src\Dhadgar.Discord\Dhadgar.Discord.csproj" />
  <ProjectReference Include="..\Dhadgar.ServiceDefaults.Tests\Dhadgar.ServiceDefaults.Tests.csproj" />
</ItemGroup>
```

### Integration Testing with WebApplicationFactory

The service exposes `public partial class Program` to enable WebApplicationFactory-based integration tests:

```csharp
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // Add test-specific configuration
        });
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }
}
```

### Planned Test Categories

| Category | Description |
|----------|-------------|
| Unit Tests | Command handlers, embed builders, validation |
| Integration Tests | API endpoints, database operations |
| Contract Tests | MassTransit message consumers |
| Bot Tests | Discord.NET interaction mocking |

### Mocking Discord.NET

For unit testing command handlers, mock the Discord client:

```csharp
// Example (planned)
[Fact]
public async Task ServerStatusCommand_ReturnsEmbed_WhenServerExists()
{
    // Arrange
    var mockInteraction = Substitute.For<ISlashCommandInteraction>();
    var mockServersClient = Substitute.For<IServersApiClient>();
    mockServersClient.GetServerAsync("my-server").Returns(new ServerDto { ... });

    var handler = new ServerCommands(mockServersClient);

    // Act
    var result = await handler.HandleStatusAsync(mockInteraction, "my-server");

    // Assert
    Assert.NotNull(result.Embed);
    Assert.Equal("MY-SERVER", result.Embed.Title);
}
```

---

## Docker

### Build Locally

```bash
# From solution root
docker build -f src/Dhadgar.Discord/Dockerfile -t dhadgar/discord:latest .
```

### Run Container

```bash
docker run -d \
  --name dhadgar-discord \
  -p 5012:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Database=dhadgar_discord" \
  -e Discord__BotToken="your-token" \
  dhadgar/discord:latest
```

### Production Dockerfile

The service has two Dockerfiles:

1. **Dockerfile** - Full multi-stage build
2. **Dockerfile.pipeline** - Uses pre-built artifacts from CI/CD

Both produce identical runtime images:

- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
- Non-root user: `appuser`
- Exposed port: `8080`
- Health check: `curl http://localhost:8080/healthz`

### Health Check Configuration

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/healthz || exit 1
```

### Container Registry

Images are pushed to Azure Container Registry:

```
meridianconsoleacr-etdvg4cthscffqdf.azurecr.io/dhadgar/discord:latest
```

---

## Troubleshooting

### Common Issues

#### Bot Not Responding to Commands

1. **Check bot token**: Ensure `Discord:BotToken` is set correctly
2. **Check intents**: Verify required intents are enabled in Developer Portal
3. **Check permissions**: Ensure bot has required permissions in the guild
4. **Check command sync**: Commands may need to be re-synced after changes

#### OAuth2 Callback Errors

1. **Check redirect URI**: Must match exactly what's configured in Discord
2. **Check client secret**: Ensure `Discord:ClientSecret` is correct
3. **Check HTTPS**: Production callbacks must use HTTPS

#### Rate Limiting

Discord has strict rate limits. The service implements:
- Per-user command rate limiting
- Global API rate limit handling
- Exponential backoff on 429 responses

#### Connection Issues

```bash
# Check service logs
dotnet run --project src/Dhadgar.Discord 2>&1 | grep -i discord

# Check health endpoint
curl -v http://localhost:5012/healthz
```

### Debug Logging

Enable verbose Discord.NET logging in development:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Discord": "Debug",
      "Discord.Net": "Debug"
    }
  }
}
```

### Useful Discord API Resources

- [Discord Developer Portal](https://discord.com/developers/applications)
- [Discord API Documentation](https://discord.com/developers/docs)
- [Discord.NET Documentation](https://discordnet.dev/)
- [Discord OAuth2 Guide](https://discord.com/developers/docs/topics/oauth2)
- [Slash Commands Guide](https://discord.com/developers/docs/interactions/application-commands)

---

## Related Documentation

- [Repository CLAUDE.md](/CLAUDE.md) - Main repository documentation
- [Gateway Service](/src/Dhadgar.Gateway/README.md) - API Gateway documentation
- [Notifications Service](/src/Dhadgar.Notifications/CLAUDE.md) - Notification system
- [Servers Service](/src/Dhadgar.Servers/CLAUDE.md) - Server management
- [Identity Service](/src/Dhadgar.Identity/README.md) - Authentication and authorization
- [Dhadgar.Contracts](/src/Shared/Dhadgar.Contracts/CLAUDE.md) - Shared message contracts

---

## Contributing

When contributing to this service:

1. Follow the existing code patterns and project structure
2. Add tests for new functionality
3. Update this README when adding new features
4. Use the `dotnet-test-engineer` agent for test guidance
5. Services must NOT reference other services via `ProjectReference`
6. All inter-service communication uses HTTP or MassTransit

---

*Last updated: January 2026*
