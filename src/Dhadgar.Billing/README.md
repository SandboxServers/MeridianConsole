# Dhadgar.Billing Service

## Overview

The **Dhadgar.Billing** service is the subscription management, usage metering, and invoicing component of the Meridian Console platform. It handles the commercial aspects of the SaaS offering, including subscription lifecycle management, payment processing integration, usage-based billing, and invoice generation.

> **Important**: This service is excluded from the **KiP (Knowledge is Power)** self-hosted edition. Organizations running the KiP edition will not deploy this service as billing functionality is not needed for self-hosted deployments.

### Business Context

Meridian Console is a multi-tenant SaaS platform that orchestrates game servers on customer-owned hardware. The Billing service enables the commercial viability of this platform by:

1. **Subscription Management**: Handling different subscription tiers that unlock varying levels of functionality and resource limits
2. **Usage Metering**: Tracking billable resources across the platform (nodes, servers, storage, bandwidth)
3. **Payment Processing**: Integrating with payment providers (primarily Stripe) for secure payment collection
4. **Invoice Generation**: Creating detailed invoices for organizations based on their subscription and usage
5. **Multi-Tenant Billing**: Supporting organization-level billing with proper isolation between tenants

### Service Role in Architecture

```text
                                    +------------------+
                                    |     Gateway      |
                                    | (YARP @ :5000)   |
                                    +--------+---------+
                                             |
                    +------------------------+------------------------+
                    |                        |                        |
           +--------v--------+     +---------v--------+     +---------v--------+
           |    Identity     |     |     Billing      |     |     Servers      |
           |   (Auth/Users)  |<--->|  (Subscriptions) |<--->|  (Game Servers)  |
           +--------+--------+     +---------+--------+     +---------+--------+
                    |                        |                        |
                    |                        v                        |
                    |              +---------+--------+               |
                    +------------->|     Nodes        |<--------------+
                                   |  (Infrastructure)|
                                   +------------------+
```

The Billing service sits as a cross-cutting concern, interfacing with:

- **Identity Service**: To validate organization context and user permissions
- **Servers Service**: To meter server instance counts and configurations
- **Nodes Service**: To meter node registrations and capacity
- **Files Service**: To meter storage consumption
- All other services: For aggregate usage tracking

---

## Current Status

> **Status: STUB SERVICE**
>
> This service contains basic scaffolding with foundational structure in place. The core billing functionality is planned but not yet implemented. This is intentional - the codebase provides the "shape" for incremental development.

### What Exists Today

| Component         | Status     | Description                                              |
| ----------------- | ---------- | -------------------------------------------------------- |
| Project Structure | Complete   | Standard .NET 10 Web API project with EF Core            |
| Database Context  | Scaffolded | `BillingDbContext` with placeholder `SampleEntity`       |
| Health Endpoints  | Complete   | `/`, `/hello`, `/healthz` endpoints                      |
| Swagger/OpenAPI   | Complete   | API documentation in Development mode                    |
| Gateway Routing   | Configured | Routes at `/api/v1/billing/*` with `TenantScoped` policy |
| Integration Tests | Complete   | Basic endpoint and Swagger verification tests            |

### What Needs Implementation

| Component          | Priority | Description                              |
| ------------------ | -------- | ---------------------------------------- |
| Subscription Plans | High     | Define plan tiers, features, and limits  |
| Stripe Integration | High     | Payment processing and webhook handling  |
| Usage Metering     | High     | Track billable resources across services |
| Invoice Generation | Medium   | Create and deliver invoices              |
| Billing History    | Medium   | Query past invoices and payments         |
| Trial Periods      | Medium   | Time-limited free trials with conversion |
| Proration          | Low      | Handle mid-cycle plan changes            |
| Tax Calculation    | Low      | Regional tax compliance                  |

---

## Technology Stack

### Core Technologies

| Technology            | Version | Purpose                 |
| --------------------- | ------- | ----------------------- |
| .NET                  | 10.0    | Runtime platform        |
| ASP.NET Core          | 10.0    | Web framework           |
| Entity Framework Core | 10.0    | ORM and database access |
| PostgreSQL            | 16      | Primary database        |
| Npgsql                | 10.0    | PostgreSQL driver       |

### Shared Libraries

| Library                   | Purpose                                     |
| ------------------------- | ------------------------------------------- |
| `Dhadgar.Contracts`       | Shared DTOs and message contracts           |
| `Dhadgar.Shared`          | Common utilities and primitives             |
| `Dhadgar.Messaging`       | MassTransit/RabbitMQ configuration          |
| `Dhadgar.ServiceDefaults` | Standard middleware, Swagger, observability |

### Messaging (Planned)

| Technology  | Version | Purpose                   |
| ----------- | ------- | ------------------------- |
| MassTransit | 8.3.6   | Message abstraction layer |
| RabbitMQ    | 3.x     | Message broker            |

### External Integrations (Planned)

| Integration     | Purpose                                     |
| --------------- | ------------------------------------------- |
| Stripe          | Payment processing, subscription management |
| Stripe Webhooks | Async payment event handling                |

---

## Quick Start

### Prerequisites

1. **.NET SDK 10.0.100** - Pinned in `global.json`
2. **Docker** - For local PostgreSQL and RabbitMQ
3. **PostgreSQL client tools** - Optional, for database inspection

### Start Local Infrastructure

From the repository root:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts:

- PostgreSQL on port `5432` (credentials: `dhadgar/dhadgar`)
- RabbitMQ on ports `5672` (AMQP) and `15672` (Management UI)
- Redis on port `6379`
- Observability stack (Grafana, Prometheus, Loki, OTLP Collector)

### Build and Run

```bash
# Build the service
dotnet build src/Dhadgar.Billing

# Run in development mode (auto-applies migrations)
dotnet run --project src/Dhadgar.Billing

# Or with hot reload
dotnet watch --project src/Dhadgar.Billing
```

The service starts on `http://localhost:5020` (configured in `launchSettings.json`).

### Verify Service Health

```bash
# Service info
curl http://localhost:5020/

# Hello endpoint
curl http://localhost:5020/hello

# Health check
curl http://localhost:5020/healthz

# Swagger UI (Development mode only)
open http://localhost:5020/swagger
```

### Access via Gateway

When running the full platform, access billing through the Gateway:

```bash
# Through Gateway (requires Gateway running on :5000)
curl http://localhost:5000/api/v1/billing/healthz
```

The Gateway strips the `/api/v1/billing` prefix before forwarding to this service.

---

## Planned Features

### Subscription Tiers and Plans

The Billing service will support a tiered subscription model:

#### Planned Tier Structure

| Tier             | Target              | Key Features                                    |
| ---------------- | ------------------- | ----------------------------------------------- |
| **Free**         | Hobbyists           | 1 node, 3 servers, basic support                |
| **Starter**      | Small communities   | 3 nodes, 10 servers, email support              |
| **Professional** | Gaming communities  | 10 nodes, 50 servers, priority support          |
| **Enterprise**   | Large organizations | Unlimited nodes/servers, dedicated support, SLA |

#### Plan Configuration (Planned Schema)

```csharp
public class SubscriptionPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; }           // "Starter", "Professional", etc.
    public string StripeProductId { get; set; } // External Stripe reference
    public string StripePriceId { get; set; }   // Monthly price in Stripe

    // Resource Limits
    public int MaxNodes { get; set; }
    public int MaxServers { get; set; }
    public int MaxMembers { get; set; }
    public long MaxStorageBytes { get; set; }

    // Feature Flags
    public bool AllowCustomDomains { get; set; }
    public bool AllowApiAccess { get; set; }
    public bool IncludesPrioritySupport { get; set; }

    public decimal MonthlyPriceCents { get; set; }
    public bool IsActive { get; set; }
}
```

### Usage Metering

The service will track billable resources across the platform:

#### Metered Dimensions

| Dimension           | Source Service | Aggregation           |
| ------------------- | -------------- | --------------------- |
| Node count          | Nodes          | Daily high-water mark |
| Server count        | Servers        | Daily high-water mark |
| Storage used (GB)   | Files          | Daily average         |
| Bandwidth (GB)      | Files, Console | Monthly total         |
| API calls           | Gateway        | Monthly total         |
| Console connections | Console        | Peak concurrent       |

#### Usage Recording (Planned)

```csharp
public class UsageRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Dimension { get; set; }      // "nodes", "servers", "storage_gb"
    public decimal Quantity { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}
```

#### Usage Collection Architecture

```text
+-------------+     +-------------+     +-------------+
|   Servers   |     |    Nodes    |     |    Files    |
+------+------+     +------+------+     +------+------+
       |                   |                   |
       +-------------------+-------------------+
                           |
                    (MassTransit Events)
                           |
                    +------v------+
                    |   Billing   |
                    | (Usage Agg) |
                    +------+------+
                           |
                    +------v------+
                    |  PostgreSQL |
                    | (Usage Data)|
                    +-------------+
```

Services publish usage events; Billing aggregates and stores them.

### Payment Processing (Stripe Integration)

#### Stripe Integration Points

| Feature         | Stripe API            | Purpose                                |
| --------------- | --------------------- | -------------------------------------- |
| Customers       | `/v1/customers`       | Link organizations to Stripe customers |
| Subscriptions   | `/v1/subscriptions`   | Manage recurring billing               |
| Payment Methods | `/v1/payment_methods` | Store cards/payment sources            |
| Invoices        | `/v1/invoices`        | Generated invoices                     |
| Webhooks        | Webhook endpoints     | Async event processing                 |

#### Webhook Events to Handle

| Event                                  | Action                                    |
| -------------------------------------- | ----------------------------------------- |
| `customer.subscription.created`        | Activate subscription, update plan        |
| `customer.subscription.updated`        | Update limits, handle upgrades/downgrades |
| `customer.subscription.deleted`        | Trigger cancellation workflow             |
| `invoice.payment_succeeded`            | Mark invoice paid, extend service         |
| `invoice.payment_failed`               | Notify customer, initiate dunning         |
| `customer.subscription.trial_will_end` | Send trial ending notification            |

#### Payment Flow (Planned)

```text
User clicks "Subscribe"
        |
        v
+-------+--------+
| Panel UI calls |
| /subscribe     |
+-------+--------+
        |
        v
+-------+--------+
| Billing creates|
| Stripe Session |
+-------+--------+
        |
        v
+-------+--------+
| Redirect to    |
| Stripe Checkout|
+-------+--------+
        |
        v
+-------+--------+
| User completes |
| payment        |
+-------+--------+
        |
        v
+-------+--------+
| Stripe Webhook |
| confirms       |
+-------+--------+
        |
        v
+-------+--------+
| Billing updates|
| subscription   |
+-------+--------+
```

### Invoicing and Billing History

#### Invoice Structure (Planned)

```csharp
public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string StripeInvoiceId { get; set; }

    public string InvoiceNumber { get; set; }    // "INV-2024-001234"
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }

    public decimal SubtotalCents { get; set; }
    public decimal TaxCents { get; set; }
    public decimal TotalCents { get; set; }

    public string Currency { get; set; }         // "USD"
    public InvoiceStatus Status { get; set; }    // Draft, Open, Paid, Void

    public string? PdfUrl { get; set; }
    public ICollection<InvoiceLineItem> LineItems { get; set; }
}

public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }

    public string Description { get; set; }      // "Professional Plan (Monthly)"
    public decimal Quantity { get; set; }
    public decimal UnitPriceCents { get; set; }
    public decimal TotalCents { get; set; }

    public string? UsageDimension { get; set; }  // For usage-based items
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}
```

### Trial Periods and Upgrades

#### Trial Configuration

- Default trial period: 14 days
- No credit card required for trial
- Full Professional tier features during trial
- Automatic downgrade to Free if not converted

#### Upgrade/Downgrade Handling

| Scenario            | Behavior                                                    |
| ------------------- | ----------------------------------------------------------- |
| Upgrade mid-cycle   | Immediate access, prorated charge                           |
| Downgrade mid-cycle | Access until period end, then downgrade                     |
| Cancel              | Access until period end, then suspend                       |
| Reactivate          | If within grace period, restore; otherwise new subscription |

### Organization Billing (Multi-Tenant)

Each organization has its own:

- Stripe Customer record
- Subscription(s)
- Payment methods
- Invoice history
- Usage tracking

#### Billing Ownership

```csharp
public class OrganizationBilling
{
    public Guid OrganizationId { get; set; }
    public string StripeCustomerId { get; set; }

    public Guid? CurrentSubscriptionId { get; set; }
    public Subscription? CurrentSubscription { get; set; }

    // Billing contact (may differ from org owner)
    public string BillingEmail { get; set; }
    public string? BillingName { get; set; }
    public Address? BillingAddress { get; set; }

    public string? TaxId { get; set; }           // VAT number, etc.
    public DateTime CreatedAt { get; set; }
}
```

---

## Database Schema

### Current Schema (Stub)

The current implementation includes only a placeholder entity:

```csharp
// Current: Placeholder only
public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Planned Schema

#### Entity Relationship Diagram (Planned)

```text
+------------------+       +------------------+       +------------------+
| OrganizationBilling |<--| Subscription     |<------| SubscriptionPlan |
+------------------+       +------------------+       +------------------+
| OrganizationId PK|       | Id PK            |       | Id PK            |
| StripeCustomerId |       | OrganizationId FK|       | Name             |
| BillingEmail     |       | PlanId FK        |       | StripeProductId  |
| CurrentSubsId FK |       | Status           |       | StripePriceId    |
+--------+---------+       | TrialEndsAt      |       | MaxNodes         |
         |                 | CurrentPeriodEnd |       | MaxServers       |
         |                 +------------------+       | MonthlyPrice     |
         |                                           +------------------+
         |
         v
+------------------+       +------------------+       +------------------+
| Invoice          |<------| InvoiceLineItem  |       | UsageRecord      |
+------------------+       +------------------+       +------------------+
| Id PK            |       | Id PK            |       | Id PK            |
| OrganizationId FK|       | InvoiceId FK     |       | OrganizationId FK|
| StripeInvoiceId  |       | Description      |       | Dimension        |
| InvoiceNumber    |       | Quantity         |       | Quantity         |
| Status           |       | UnitPrice        |       | RecordedAt       |
| TotalCents       |       | TotalCents       |       | PeriodStart      |
+------------------+       +------------------+       +------------------+
                                                              |
                                                              v
                                                     +------------------+
                                                     | PaymentMethod    |
                                                     +------------------+
                                                     | Id PK            |
                                                     | OrganizationId FK|
                                                     | StripePaymentId  |
                                                     | Type             |
                                                     | Last4            |
                                                     | IsDefault        |
                                                     +------------------+
```

#### Planned Entities Summary

| Entity                | Purpose                                                |
| --------------------- | ------------------------------------------------------ |
| `OrganizationBilling` | Links organization to Stripe customer, billing contact |
| `SubscriptionPlan`    | Defines available plans with limits and pricing        |
| `Subscription`        | Active subscription linking org to plan                |
| `Invoice`             | Generated invoices with status tracking                |
| `InvoiceLineItem`     | Individual charges on an invoice                       |
| `UsageRecord`         | Time-series usage data for metering                    |
| `PaymentMethod`       | Stored payment methods (cards, etc.)                   |
| `BillingEvent`        | Audit trail of billing actions                         |

### Database Migrations

#### Current State

No migrations exist yet. The `Migrations/` folder contains a placeholder README.

#### Creating Migrations

```bash
# From repository root
dotnet ef migrations add InitialCreate \
  --project src/Dhadgar.Billing \
  --startup-project src/Dhadgar.Billing \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Billing \
  --startup-project src/Dhadgar.Billing
```

#### Auto-Migration in Development

The service automatically applies pending migrations when running in Development mode:

```csharp
// From Program.cs
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    db.Database.Migrate();
}
```

This is disabled in production - use explicit migration commands for production deployments.

---

## API Endpoints

### Current Endpoints

| Method | Path       | Description  | Response                                                                |
| ------ | ---------- | ------------ | ----------------------------------------------------------------------- |
| GET    | `/`        | Service info | `{ service: "Dhadgar.Billing", message: "Hello from Dhadgar.Billing" }` |
| GET    | `/hello`   | Hello world  | `"Hello from Dhadgar.Billing"`                                          |
| GET    | `/healthz` | Health check | `{ service: "Dhadgar.Billing", status: "ok" }`                          |

### Planned Endpoints

#### Subscription Management

| Method | Path                       | Description                             |
| ------ | -------------------------- | --------------------------------------- |
| GET    | `/plans`                   | List available subscription plans       |
| GET    | `/plans/{id}`              | Get plan details                        |
| GET    | `/subscription`            | Get current organization subscription   |
| POST   | `/subscription`            | Create new subscription (checkout)      |
| PUT    | `/subscription`            | Update subscription (upgrade/downgrade) |
| DELETE | `/subscription`            | Cancel subscription                     |
| POST   | `/subscription/reactivate` | Reactivate cancelled subscription       |

#### Billing Information

| Method | Path                                    | Description                    |
| ------ | --------------------------------------- | ------------------------------ |
| GET    | `/billing`                              | Get organization billing info  |
| PUT    | `/billing`                              | Update billing contact/address |
| GET    | `/billing/payment-methods`              | List saved payment methods     |
| POST   | `/billing/payment-methods`              | Add payment method             |
| DELETE | `/billing/payment-methods/{id}`         | Remove payment method          |
| PUT    | `/billing/payment-methods/{id}/default` | Set default payment method     |

#### Invoices

| Method | Path                 | Description                   |
| ------ | -------------------- | ----------------------------- |
| GET    | `/invoices`          | List invoices with pagination |
| GET    | `/invoices/{id}`     | Get invoice details           |
| GET    | `/invoices/{id}/pdf` | Download invoice PDF          |
| GET    | `/invoices/upcoming` | Preview next invoice          |

#### Usage

| Method | Path             | Description                       |
| ------ | ---------------- | --------------------------------- |
| GET    | `/usage`         | Get current period usage summary  |
| GET    | `/usage/history` | Get historical usage data         |
| GET    | `/usage/limits`  | Get current plan limits vs. usage |

#### Webhooks (Internal)

| Method | Path               | Description             |
| ------ | ------------------ | ----------------------- |
| POST   | `/webhooks/stripe` | Stripe webhook endpoint |

### Gateway Routing

The Gateway routes billing requests with the following configuration:

```json
{
  "billing-route": {
    "ClusterId": "billing",
    "Order": 20,
    "Match": { "Path": "/api/v1/billing/{**catch-all}" },
    "AuthorizationPolicy": "TenantScoped",
    "RateLimiterPolicy": "PerTenant",
    "Transforms": [{ "PathRemovePrefix": "/api/v1/billing" }]
  }
}
```

**Key Points:**

- Requires `TenantScoped` authorization (user must belong to an organization)
- Rate limited per tenant
- Path prefix `/api/v1/billing` is stripped before forwarding

---

## Integration Points

### Identity Service Integration

The Billing service requires integration with Identity for:

#### User Context

- Validating the authenticated user
- Extracting organization context from JWT claims
- Checking `org:billing` permission for billing management operations

#### Permission Required

The `org:billing` claim (defined in Identity's claim definitions) is required to:

- View billing information
- Manage payment methods
- Change subscription plans
- View invoices

```csharp
// Example endpoint authorization
app.MapGet("/billing", async (HttpContext context, BillingService billing) =>
{
    var orgId = context.GetOrganizationId(); // From claims
    if (!context.HasPermission("org:billing"))
        return Results.Forbid();

    return await billing.GetBillingInfoAsync(orgId);
});
```

#### Organization Lifecycle Events

Subscribe to Identity events via MassTransit:

| Event                  | Billing Action                            |
| ---------------------- | ----------------------------------------- |
| `OrganizationCreated`  | Create billing record, apply free tier    |
| `OrganizationDeleted`  | Cancel subscription, archive billing data |
| `OwnershipTransferred` | Update billing contact notification       |

### Service-to-Service Communication

#### Consuming Usage Events

Other services publish usage events that Billing aggregates:

```csharp
// Example: Servers service publishes
public record ServerCreated(Guid ServerId, Guid OrganizationId, DateTime CreatedAt);
public record ServerDeleted(Guid ServerId, Guid OrganizationId, DateTime DeletedAt);

// Billing subscribes
public class ServerCreatedConsumer : IConsumer<ServerCreated>
{
    public async Task Consume(ConsumeContext<ServerCreated> context)
    {
        await _usageService.RecordUsageAsync(
            context.Message.OrganizationId,
            "servers",
            1,
            context.Message.CreatedAt);
    }
}
```

#### Enforcement Callbacks

When usage limits are exceeded, Billing publishes events for enforcement:

```csharp
// Billing publishes when limit exceeded
public record UsageLimitExceeded(
    Guid OrganizationId,
    string Dimension,      // "nodes", "servers"
    int CurrentUsage,
    int Limit,
    DateTimeOffset OccurredAt);

// Servers service can subscribe to block new server creation
```

### Stripe Integration

#### Configuration

```json
// appsettings.json (planned)
{
  "Stripe": {
    "SecretKey": "sk_test_...", // Use user-secrets in dev!
    "WebhookSecret": "whsec_...",
    "PublishableKey": "pk_test_..."
  }
}
```

**Important**: Never commit Stripe keys. Use `dotnet user-secrets`:

```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/Dhadgar.Billing
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project src/Dhadgar.Billing
```

#### Stripe Client Setup (Planned)

```csharp
// In Program.cs
builder.Services.AddSingleton<IStripeClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new StripeClient(config["Stripe:SecretKey"]);
});

builder.Services.AddScoped<StripeSubscriptionService>();
builder.Services.AddScoped<StripeCustomerService>();
builder.Services.AddScoped<StripeInvoiceService>();
```

---

## Configuration

### Configuration File

**`appsettings.json`**:

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

### Environment-Specific Configuration

Create `appsettings.Development.json` for local overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Dhadgar.Billing": "Debug"
    }
  }
}
```

### Planned Configuration Options

```json
{
  "Billing": {
    "DefaultTrialDays": 14,
    "GracePeriodDays": 7,
    "EnableUsageMetering": true,
    "MeteringSyncIntervalMinutes": 15
  },
  "Stripe": {
    "SecretKey": "USE_USER_SECRETS",
    "WebhookSecret": "USE_USER_SECRETS",
    "PublishableKey": "pk_test_...",
    "EnableTestMode": true
  },
  "Invoicing": {
    "CompanyName": "Meridian Console",
    "CompanyAddress": "...",
    "TaxIdLabel": "VAT",
    "DefaultCurrency": "USD"
  }
}
```

### User Secrets for Sensitive Values

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Billing

# Set secrets
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/Dhadgar.Billing
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project src/Dhadgar.Billing

# List secrets
dotnet user-secrets list --project src/Dhadgar.Billing
```

### Environment Variables

In production, configure via environment variables:

| Variable                      | Description                   |
| ----------------------------- | ----------------------------- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string  |
| `Stripe__SecretKey`           | Stripe API secret key         |
| `Stripe__WebhookSecret`       | Stripe webhook signing secret |
| `RabbitMq__Host`              | RabbitMQ hostname             |
| `RabbitMq__Username`          | RabbitMQ username             |
| `RabbitMq__Password`          | RabbitMQ password             |

---

## Testing

### Test Project Structure

```text
tests/Dhadgar.Billing.Tests/
    BillingWebApplicationFactory.cs   # Test server configuration
    HelloWorldTests.cs                # Basic unit tests
    SwaggerTests.cs                   # API documentation tests
    Dhadgar.Billing.Tests.csproj
```

### Running Tests

```bash
# Run all Billing tests
dotnet test tests/Dhadgar.Billing.Tests

# Run specific test
dotnet test tests/Dhadgar.Billing.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbose output
dotnet test tests/Dhadgar.Billing.Tests --logger "console;verbosity=detailed"
```

### Test Configuration

The `BillingWebApplicationFactory` configures an in-memory database for tests:

```csharp
public class BillingWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with in-memory database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BillingDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BillingDbContext>(options =>
            {
                options.UseInMemoryDatabase("BillingTestDb");
            });
        });
    }
}
```

### Current Tests

#### HelloWorldTests

Verifies the `Hello.Message` constant:

```csharp
[Fact]
public void Hello_message_is_correct()
{
    Assert.Equal("Hello from Dhadgar.Billing", Hello.Message);
}
```

#### SwaggerTests

Uses shared `SwaggerTestHelper` from `Dhadgar.ServiceDefaults.Tests`:

```csharp
[Fact]
public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
{
    await SwaggerTestHelper.VerifySwaggerEndpointAsync(
        _factory,
        expectedTitle: "Dhadgar Billing API");
}

[Fact]
public async Task SwaggerUi_ReturnsHtml()
{
    await SwaggerTestHelper.VerifySwaggerUiAsync(_factory);
}

[Fact]
public async Task SwaggerEndpoint_DocumentsHealthEndpoints()
{
    await SwaggerTestHelper.VerifyHealthEndpointsDocumentedAsync(_factory);
}
```

### Planned Test Categories

| Category          | Purpose                                       |
| ----------------- | --------------------------------------------- |
| Unit Tests        | Test individual services, validators          |
| Integration Tests | Test database operations, API endpoints       |
| Stripe Mock Tests | Test payment flows with mock Stripe responses |
| Contract Tests    | Verify message contract compatibility         |

### Testing Stripe Integration

For Stripe integration tests, use Stripe's test mode with test card numbers:

```csharp
// Example test using Stripe test mode
[Fact]
public async Task CreateSubscription_WithValidCard_Succeeds()
{
    // Arrange
    var paymentMethod = await _stripe.PaymentMethodService.CreateAsync(new PaymentMethodCreateOptions
    {
        Type = "card",
        Card = new PaymentMethodCardOptions
        {
            Number = "4242424242424242",  // Stripe test card
            ExpMonth = 12,
            ExpYear = 2030,
            Cvc = "123"
        }
    });

    // Act & Assert
    // ...
}
```

---

## Development Workflow

### Adding New Endpoints

1. Define the endpoint in `Program.cs` using Minimal API style
2. Add Swagger tags for documentation grouping
3. Add corresponding test in `tests/Dhadgar.Billing.Tests`

Example:

```csharp
// In Program.cs
app.MapGet("/plans", async (BillingDbContext db) =>
{
    var plans = await db.SubscriptionPlans
        .Where(p => p.IsActive)
        .ToListAsync();
    return Results.Ok(plans);
})
.WithTags("Plans")
.WithName("ListPlans")
.Produces<List<SubscriptionPlan>>(StatusCodes.Status200OK);
```

### Adding Database Entities

1. Define entity class in `Data/Entities/`
2. Add `DbSet<T>` property to `BillingDbContext`
3. Create configuration in `Data/Configuration/` (optional, for complex mappings)
4. Add migration

```bash
dotnet ef migrations add AddSubscriptionPlan \
  --project src/Dhadgar.Billing \
  --startup-project src/Dhadgar.Billing \
  --output-dir Data/Migrations
```

### Adding Message Consumers

1. Define message contract in `Dhadgar.Contracts`
2. Create consumer class implementing `IConsumer<T>`
3. Register in MassTransit configuration

```csharp
// In Program.cs
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ServerCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]);
            h.Password(builder.Configuration["RabbitMq:Password"]);
        });

        cfg.ConfigureEndpoints(context);
    });
});
```

---

## Observability

### Logging

The service uses structured logging with correlation IDs from `Dhadgar.ServiceDefaults`:

```csharp
// Logs automatically include:
// - CorrelationId: Request correlation
// - RequestId: Individual request ID
// - TraceId: OpenTelemetry trace ID
```

### Metrics (Planned)

Custom billing metrics to expose:

| Metric                         | Type    | Description                          |
| ------------------------------ | ------- | ------------------------------------ |
| `billing_subscriptions_active` | Gauge   | Current active subscriptions by plan |
| `billing_invoices_generated`   | Counter | Invoices generated                   |
| `billing_payments_processed`   | Counter | Successful payments                  |
| `billing_payments_failed`      | Counter | Failed payments                      |
| `billing_mrr_cents`            | Gauge   | Monthly recurring revenue            |

### Health Checks (Planned)

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddRabbitMQ(name: "rabbitmq")
    .AddCheck<StripeHealthCheck>("stripe");
```

---

## Security Considerations

### Sensitive Data Handling

| Data Type         | Handling                                                    |
| ----------------- | ----------------------------------------------------------- |
| Stripe API Keys   | User secrets / environment variables, never in config files |
| Payment Card Data | Never stored - Stripe handles PCI compliance                |
| Invoice PDFs      | Generated by Stripe, linked by URL                          |
| Customer Email    | Stored encrypted at rest                                    |

### Authorization

All billing endpoints require:

1. Valid JWT token (authenticated user)
2. Organization context (multi-tenant)
3. `org:billing` permission for management operations

### Webhook Security

Stripe webhooks must be verified using the webhook signing secret:

```csharp
app.MapPost("/webhooks/stripe", async (HttpRequest request, IConfiguration config) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    var signature = request.Headers["Stripe-Signature"];

    var stripeEvent = EventUtility.ConstructEvent(
        json,
        signature,
        config["Stripe:WebhookSecret"]);

    // Process event...
});
```

---

## Related Documentation

### Internal Documentation

| Document                 | Path                                 | Description                       |
| ------------------------ | ------------------------------------ | --------------------------------- |
| Main README              | `/CLAUDE.md`                         | Project overview and architecture |
| Development Setup        | `/docs/DEVELOPMENT_SETUP.md`         | Environment setup guide           |
| Configuration Management | `/docs/CONFIGURATION-MANAGEMENT.md`  | Config best practices             |
| Identity API Reference   | `/docs/identity-api-reference.md`    | Identity service API              |
| Identity Claims          | `/docs/identity-claims-reference.md` | Permission claims                 |

### Infrastructure

| Document       | Path                                     | Description                  |
| -------------- | ---------------------------------------- | ---------------------------- |
| Docker Compose | `/deploy/compose/docker-compose.dev.yml` | Local infrastructure         |
| Compose README | `/deploy/compose/README.md`              | Infrastructure documentation |

### Shared Libraries

| Library         | Path                                   | Description               |
| --------------- | -------------------------------------- | ------------------------- |
| Contracts       | `/src/Shared/Dhadgar.Contracts/`       | Shared DTOs and messages  |
| ServiceDefaults | `/src/Shared/Dhadgar.ServiceDefaults/` | Common middleware         |
| Messaging       | `/src/Shared/Dhadgar.Messaging/`       | MassTransit configuration |

### External Resources

| Resource         | URL                                                          | Description              |
| ---------------- | ------------------------------------------------------------ | ------------------------ |
| Stripe API Docs  | [stripe.com/docs/api](https://stripe.com/docs/api)           | Stripe API reference     |
| Stripe Testing   | [stripe.com/docs/testing](https://stripe.com/docs/testing)   | Test cards and scenarios |
| Stripe Webhooks  | [stripe.com/docs/webhooks](https://stripe.com/docs/webhooks) | Webhook integration      |
| MassTransit Docs | [masstransit.io](https://masstransit.io/)                    | Messaging framework      |

---

## Troubleshooting

### Common Issues

#### Database Connection Failed

```text
Error: Npgsql.NpgsqlException: Failed to connect to localhost:5432
```

**Solution**: Ensure PostgreSQL is running:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d postgres
```

#### Migration Failed

```text
Error: No migrations configuration found
```

**Solution**: Ensure you're running from the correct directory with proper project references:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Dhadgar.Billing \
  --startup-project src/Dhadgar.Billing \
  --output-dir Data/Migrations
```

#### Swagger Not Available

Swagger is only enabled in Development and Testing environments. Check:

1. `ASPNETCORE_ENVIRONMENT` is set to `Development`
2. Service is running on correct port (5020)

#### Gateway Routing Issues

If requests to `/api/v1/billing/*` return 502 or 503:

1. Verify Billing service is running on port 5020
2. Check Gateway logs for health check failures
3. Verify Gateway's `appsettings.json` has correct cluster configuration

---

## Changelog

### Current Version (Stub)

- Basic project scaffolding
- EF Core with PostgreSQL configured
- Health check endpoints
- Swagger documentation
- Integration test framework
- Gateway routing configured

### Planned Releases

| Version | Features                               |
| ------- | -------------------------------------- |
| v0.1.0  | Subscription plans, basic CRUD         |
| v0.2.0  | Stripe integration, checkout flow      |
| v0.3.0  | Usage metering, limit enforcement      |
| v0.4.0  | Invoicing, billing history             |
| v1.0.0  | Production-ready with full feature set |

---

## Contributing

When contributing to the Billing service:

1. **Follow the Architecture Rules**: Services must not reference each other via `ProjectReference`. Use messaging and HTTP for communication.

2. **Security First**: Never commit secrets. Use user-secrets for local development.

3. **Test Coverage**: Add tests for all new endpoints and business logic.

4. **Document APIs**: Update this README and Swagger annotations for new endpoints.

5. **Use Migrations**: Never modify the database schema directly. Always use EF Core migrations.

For questions about billing architecture, consult with the team lead or review the architecture documents in `/docs/architecture/`.
