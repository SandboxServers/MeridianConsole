# Phase 4: Health & Alerting - Research

**Researched:** 2026-01-22
**Domain:** ASP.NET Core Health Checks, Kubernetes Probes, Grafana Alerting, Discord/Email Notifications
**Confidence:** HIGH

## Summary

This research covers the health check and alerting infrastructure needed for Phase 4. The ASP.NET Core ecosystem has mature, well-established patterns for health checks through the `AspNetCore.Diagnostics.HealthChecks` family of packages. The codebase already has foundational health check infrastructure in `ServiceDefaultsExtensions.cs` with `/healthz`, `/livez`, and `/readyz` endpoints configured with proper tags for Kubernetes probe differentiation.

The alerting requirements split into two domains: (1) Grafana-provisioned alert rules for the observability stack, and (2) application-level alerting for critical errors via Discord webhooks and email. Grafana supports YAML-based alert provisioning that integrates with the existing Loki and Prometheus data sources.

**Primary recommendation:** Use the established `AspNetCore.HealthChecks.*` NuGet packages (v9.0.0) for PostgreSQL, Redis, and RabbitMQ health checks. Build a lightweight alerting service in `Dhadgar.Notifications` that subscribes to critical log events and dispatches to Discord webhooks and SMTP email. Provision Grafana alerts via YAML files in `deploy/compose/grafana/provisioning/alerting/`.

## Standard Stack

The established libraries/tools for this domain:

### Core Health Check Packages
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `AspNetCore.HealthChecks.NpgSql` | 9.0.0 | PostgreSQL connectivity check | Official community package, 4.3k+ stars, actively maintained |
| `AspNetCore.HealthChecks.Redis` | 9.0.0 | Redis connectivity and latency check | Works with StackExchange.Redis already in use |
| `AspNetCore.HealthChecks.Rabbitmq` | 9.0.0 | RabbitMQ connectivity check | Supports MassTransit's IConnection reuse pattern |
| `AspNetCore.HealthChecks.UI.Client` | 9.0.0 | JSON response writer for health check UI | Standard JSON format for monitoring tools |

### Email and Notifications
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `MailKit` | 4.11.0 | SMTP email sending | Microsoft-recommended replacement for obsolete System.Net.Mail.SmtpClient |
| `MimeKit` | 4.11.0 | Email message composition | Required by MailKit, handles MIME properly |

### Alerting Infrastructure
| Tool | Purpose | Integration Point |
|------|---------|-------------------|
| Grafana Alerting | Log and metric-based alert rules | Provisioned via YAML in `/etc/grafana/provisioning/alerting/` |
| Loki LogQL | Error rate queries for alerting | `count_over_time({level="error"} [5m])` patterns |
| Prometheus PromQL | Metric-based alerting | HTTP error rate, response time thresholds |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| MailKit | FluentEmail | FluentEmail is simpler but less control over SMTP behavior |
| Custom Discord webhook | Serilog.Sinks.Discord | Serilog sinks couple alerting to logging; prefer explicit alert service |
| Grafana alerting | AlertManager standalone | Extra component to maintain; Grafana alerting is built-in |

**Installation:**
```bash
# Add to Directory.Packages.props
# <PackageVersion Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
# <PackageVersion Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
# <PackageVersion Include="AspNetCore.HealthChecks.Rabbitmq" Version="9.0.0" />
# <PackageVersion Include="AspNetCore.HealthChecks.UI.Client" Version="9.0.0" />
# <PackageVersion Include="MailKit" Version="4.11.0" />

dotnet add src/Shared/Dhadgar.ServiceDefaults package AspNetCore.HealthChecks.NpgSql
dotnet add src/Shared/Dhadgar.ServiceDefaults package AspNetCore.HealthChecks.Redis
dotnet add src/Shared/Dhadgar.ServiceDefaults package AspNetCore.HealthChecks.Rabbitmq
dotnet add src/Dhadgar.Notifications package MailKit
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── Shared/Dhadgar.ServiceDefaults/
│   ├── Health/
│   │   ├── PostgresHealthCheck.cs      # Custom check wrapping AddNpgSql
│   │   ├── RedisHealthCheck.cs         # Custom check wrapping AddRedis
│   │   └── RabbitMqHealthCheck.cs      # Custom check wrapping AddRabbitMQ
│   └── ServiceDefaultsExtensions.cs    # Extended with dependency checks
├── Dhadgar.Notifications/
│   ├── Alerting/
│   │   ├── IAlertDispatcher.cs
│   │   ├── DiscordAlertDispatcher.cs
│   │   ├── EmailAlertDispatcher.cs
│   │   └── CompositeAlertDispatcher.cs
│   ├── Email/
│   │   ├── IEmailSender.cs
│   │   ├── SmtpEmailSender.cs
│   │   └── EmailOptions.cs
│   └── Discord/
│       ├── IDiscordWebhook.cs
│       ├── DiscordWebhookClient.cs
│       └── DiscordOptions.cs
└── deploy/compose/grafana/provisioning/
    └── alerting/
        ├── alert-rules.yaml            # Error rate alert rules
        ├── contact-points.yaml         # Discord + email contact points
        └── notification-policies.yaml  # Routing rules
```

### Pattern 1: Health Check Tags for Kubernetes Probes
**What:** Use tags to differentiate liveness vs readiness checks
**When to use:** All services with external dependencies
**Example:**
```csharp
// Source: AspNetCore.Diagnostics.HealthChecks documentation
builder.Services.AddHealthChecks()
    // Liveness: Only checks if app is running (fast, no external deps)
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])

    // Readiness: Checks external dependencies (slower, validates connectivity)
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgres",
        tags: ["ready"])
    .AddRedis(
        redisConnectionString: builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: ["ready"])
    .AddRabbitMQ(
        rabbitConnectionString: new Uri(builder.Configuration.GetConnectionString("RabbitMqHost")!),
        name: "rabbitmq",
        tags: ["ready"]);
```

### Pattern 2: Service-Specific Health Check Registration
**What:** Each service registers only the dependencies it actually uses
**When to use:** Database-backed, cache-using, or messaging services
**Example:**
```csharp
// In ServiceDefaultsExtensions.cs - Extension methods for optional dependencies
public static IHealthChecksBuilder AddPostgresHealthCheck(
    this IHealthChecksBuilder builder,
    IConfiguration config)
{
    var connectionString = config.GetConnectionString("Postgres");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        builder.AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);
    }
    return builder;
}
```

### Pattern 3: Discord Webhook with HttpClient
**What:** Send alerts via Discord webhook using typed HttpClient
**When to use:** Critical error notifications
**Example:**
```csharp
// Source: Discord webhook API documentation
public sealed class DiscordWebhookClient : IDiscordWebhook
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;

    public DiscordWebhookClient(HttpClient httpClient, IOptions<DiscordOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendAlertAsync(AlertMessage alert, CancellationToken ct = default)
    {
        var payload = new
        {
            username = "Meridian Alerts",
            embeds = new[]
            {
                new
                {
                    title = alert.Title,
                    description = alert.Message,
                    color = alert.Severity == AlertSeverity.Critical ? 0xFF0000 : 0xFFA500,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    fields = new[]
                    {
                        new { name = "Service", value = alert.ServiceName, inline = true },
                        new { name = "TraceId", value = alert.TraceId, inline = true },
                        new { name = "CorrelationId", value = alert.CorrelationId, inline = true }
                    }
                }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        await _httpClient.PostAsync(_options.WebhookUrl, content, ct);
    }
}
```

### Pattern 4: MailKit SMTP Email Sender
**What:** Send email notifications using MailKit
**When to use:** Critical error email alerts
**Example:**
```csharp
// Source: MailKit documentation
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAlertEmailAsync(AlertMessage alert, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        message.To.Add(new MailboxAddress(null, _options.AlertRecipient));
        message.Subject = $"[{alert.Severity}] {alert.ServiceName}: {alert.Title}";

        message.Body = new TextPart("html")
        {
            Text = $@"
                <h2>{alert.Title}</h2>
                <p>{alert.Message}</p>
                <hr/>
                <p><strong>Service:</strong> {alert.ServiceName}</p>
                <p><strong>TraceId:</strong> {alert.TraceId}</p>
                <p><strong>CorrelationId:</strong> {alert.CorrelationId}</p>
                <p><strong>Timestamp:</strong> {alert.Timestamp:O}</p>"
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort,
            SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
```

### Pattern 5: Grafana Alert Rule YAML
**What:** Provision alert rules via configuration files
**When to use:** Error rate spike detection
**Example:**
```yaml
# deploy/compose/grafana/provisioning/alerting/alert-rules.yaml
apiVersion: 1

groups:
  - orgId: 1
    name: dhadgar-error-alerts
    folder: Dhadgar Alerts
    interval: 1m
    rules:
      - uid: error-rate-spike
        title: High Error Rate Detected
        condition: C
        data:
          - refId: A
            relativeTimeRange:
              from: 300
              to: 0
            datasourceUid: loki
            model:
              expr: 'sum(count_over_time({service_name=~"Dhadgar.*"} |= "level=Error" [5m]))'
              queryType: range
          - refId: B
            relativeTimeRange:
              from: 300
              to: 0
            datasourceUid: __expr__
            model:
              expression: A
              type: reduce
              reducer: last
          - refId: C
            datasourceUid: __expr__
            model:
              expression: B
              type: threshold
              conditions:
                - evaluator:
                    params:
                      - 10
                    type: gt
        noDataState: OK
        execErrState: Alerting
        for: 2m
        annotations:
          summary: 'Error rate exceeded threshold in {{ $labels.service_name }}'
        labels:
          severity: warning
```

### Anti-Patterns to Avoid
- **Heavy liveness checks:** Never include database/Redis checks in liveness probes. Liveness should only verify the process is responsive.
- **Single check for all:** Don't use one endpoint for both liveness and readiness. Kubernetes needs to differentiate between "app crashed" and "app warming up."
- **Synchronous webhook calls in request path:** Never call Discord/email webhooks synchronously during request processing. Use a background queue.
- **Hardcoded webhook URLs:** Always use configuration/secrets for webhook URLs and SMTP credentials.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| PostgreSQL connectivity check | Raw `NpgsqlConnection.OpenAsync()` | `AspNetCore.HealthChecks.NpgSql` | Handles timeouts, connection pooling, proper error reporting |
| Redis ping check | Manual `IDatabase.PingAsync()` | `AspNetCore.HealthChecks.Redis` | Already built in Identity service but package provides tagging, timeout config |
| RabbitMQ check | `IConnection.IsOpen` check | `AspNetCore.HealthChecks.Rabbitmq` | Proper singleton connection handling per RabbitMQ best practices |
| Health check JSON response | Custom serialization | `UIResponseWriter.WriteHealthCheckUIResponse` | Standard format monitoring tools expect |
| SMTP email | `System.Net.Mail.SmtpClient` | MailKit | SmtpClient is obsolete; MailKit handles modern auth (OAuth2, app passwords) |
| Discord webhook payload | Manual JSON construction | Define typed payload classes | Discord API has specific embed format requirements |

**Key insight:** The health check packages in `AspNetCore.Diagnostics.HealthChecks` handle edge cases around connection timeouts, proper resource disposal, and Kubernetes-compatible response formats that would take significant effort to replicate correctly.

## Common Pitfalls

### Pitfall 1: RabbitMQ Connection Churn
**What goes wrong:** Creating new RabbitMQ connections per health check causes connection exhaustion
**Why it happens:** RabbitMQ health check creates a new connection if not configured to reuse
**How to avoid:** Configure health check to use the same `IConnection` singleton that MassTransit uses:
```csharp
// Register the connection factory from MassTransit config
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new ConnectionFactory
    {
        HostName = config.GetConnectionString("RabbitMqHost") ?? "localhost",
        UserName = config["RabbitMq:Username"] ?? "dhadgar",
        Password = config["RabbitMq:Password"] ?? "dhadgar"
    };
});

// Health check uses the singleton
builder.Services.AddHealthChecks()
    .AddRabbitMQ(tags: ["ready"]);
```
**Warning signs:** RabbitMQ logs showing "connection created/closed" every 10 seconds

### Pitfall 2: Liveness Probe Kills Healthy Pods During Startup
**What goes wrong:** Kubernetes kills pods that are still initializing because liveness check fails
**Why it happens:** Liveness probe runs before readiness, no startup probe configured
**How to avoid:** Configure Kubernetes probes with proper initial delays and use startup probes:
```yaml
startupProbe:
  httpGet:
    path: /healthz/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 5
  failureThreshold: 30  # 30 * 5s = 150s max startup time

livenessProbe:
  httpGet:
    path: /healthz/live
    port: 8080
  periodSeconds: 10
  failureThreshold: 3
  # No initialDelaySeconds - startup probe handles this

readinessProbe:
  httpGet:
    path: /healthz/ready
    port: 8080
  periodSeconds: 10
  failureThreshold: 3
```
**Warning signs:** CrashLoopBackOff on slow-starting services

### Pitfall 3: Alert Storms
**What goes wrong:** Single incident triggers hundreds of Discord/email notifications
**Why it happens:** No rate limiting or deduplication on alert dispatch
**How to avoid:** Implement alert throttling with a sliding window:
```csharp
public sealed class ThrottledAlertDispatcher : IAlertDispatcher
{
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertTimes = new();
    private readonly TimeSpan _throttleWindow = TimeSpan.FromMinutes(5);

    public async Task DispatchAsync(AlertMessage alert, CancellationToken ct)
    {
        var key = $"{alert.ServiceName}:{alert.Title}";
        var now = DateTime.UtcNow;

        if (_lastAlertTimes.TryGetValue(key, out var lastTime)
            && now - lastTime < _throttleWindow)
        {
            return; // Throttled
        }

        _lastAlertTimes[key] = now;
        await _innerDispatcher.DispatchAsync(alert, ct);
    }
}
```
**Warning signs:** "Too many requests" from Discord API, flooded inbox

### Pitfall 4: Grafana Alert Rule Import Conflicts
**What goes wrong:** Grafana fails to start or alerts duplicate
**Why it happens:** Provisioned alert UIDs conflict with manually created alerts
**How to avoid:** Use distinct UID prefixes for provisioned resources and delete manual alerts before provisioning:
```yaml
rules:
  - uid: provisioned-error-rate-spike  # Prefix with "provisioned-"
```
**Warning signs:** Grafana startup logs showing "conflict" errors

### Pitfall 5: Health Check Timeouts Cascading
**What goes wrong:** One slow dependency (e.g., cold Redis) causes all health checks to timeout
**Why it happens:** Default health check timeout is too long, checks run sequentially
**How to avoid:** Set individual timeouts and use built-in health check timeout:
```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres",
        timeout: TimeSpan.FromSeconds(3), tags: ["ready"])
    .AddRedis(redisConnectionString, name: "redis",
        timeout: TimeSpan.FromSeconds(2), tags: ["ready"]);

// Set overall health check timeout
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).WithRequestTimeout(TimeSpan.FromSeconds(10));
```
**Warning signs:** Health check endpoint timing out after 30+ seconds

## Code Examples

Verified patterns from official sources:

### Service-Specific Health Check Registration
```csharp
// Source: ServiceDefaultsExtensions.cs (existing pattern) extended
public static IServiceCollection AddDhadgarServiceDefaults(
    this IServiceCollection services,
    IConfiguration configuration,
    HealthCheckDependencies dependencies = HealthCheckDependencies.None)
{
    var healthChecks = services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    if (dependencies.HasFlag(HealthCheckDependencies.Postgres))
    {
        var connStr = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrEmpty(connStr))
        {
            healthChecks.AddNpgSql(connStr, name: "postgres",
                timeout: TimeSpan.FromSeconds(3), tags: ["ready"]);
        }
    }

    if (dependencies.HasFlag(HealthCheckDependencies.Redis))
    {
        var connStr = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrEmpty(connStr))
        {
            healthChecks.AddRedis(connStr, name: "redis",
                timeout: TimeSpan.FromSeconds(2), tags: ["ready"]);
        }
    }

    if (dependencies.HasFlag(HealthCheckDependencies.RabbitMq))
    {
        healthChecks.AddRabbitMQ(name: "rabbitmq",
            timeout: TimeSpan.FromSeconds(3), tags: ["ready"]);
    }

    // ... rest of service defaults
    return services;
}

[Flags]
public enum HealthCheckDependencies
{
    None = 0,
    Postgres = 1,
    Redis = 2,
    RabbitMq = 4
}
```

### Grafana Contact Point for Discord
```yaml
# deploy/compose/grafana/provisioning/alerting/contact-points.yaml
apiVersion: 1

contactPoints:
  - orgId: 1
    name: dhadgar-alerts
    receivers:
      - uid: discord-alerts
        type: discord
        settings:
          url: ${DISCORD_WEBHOOK_URL}
          use_discord_username: false
          message: |
            **{{ .Status | toUpper }}** - {{ .CommonLabels.alertname }}

            {{ range .Alerts }}
            **Summary:** {{ .Annotations.summary }}
            **Service:** {{ .Labels.service_name }}
            **Severity:** {{ .Labels.severity }}
            {{ end }}
      - uid: email-alerts
        type: email
        settings:
          addresses: ${ALERT_EMAIL_ADDRESSES}
          singleEmail: true
```

### Kubernetes Deployment Health Probes
```yaml
# Example for Dhadgar.Servers deployment
spec:
  containers:
    - name: servers
      ports:
        - containerPort: 8080
      startupProbe:
        httpGet:
          path: /healthz/live
          port: 8080
        initialDelaySeconds: 5
        periodSeconds: 5
        failureThreshold: 30
      livenessProbe:
        httpGet:
          path: /healthz/live
          port: 8080
        periodSeconds: 15
        timeoutSeconds: 5
        failureThreshold: 3
      readinessProbe:
        httpGet:
          path: /healthz/ready
          port: 8080
        periodSeconds: 10
        timeoutSeconds: 10
        failureThreshold: 3
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| System.Net.Mail.SmtpClient | MailKit | .NET 5.0 (2020) | SmtpClient is obsolete, MailKit handles OAuth2, modern TLS |
| Custom health check logic | AspNetCore.HealthChecks.* packages | 2019+ | Standard packages handle timeouts, tagging, connection reuse |
| Alertmanager + Prometheus | Grafana Unified Alerting | Grafana 9.0 (2022) | Single pane for metrics and log-based alerts |
| Serilog sinks for alerting | Explicit alert service | Current best practice | Decouples logging from alerting, allows throttling/dedup |

**Deprecated/outdated:**
- `System.Net.Mail.SmtpClient`: Microsoft recommends MailKit; will emit compiler warnings
- Grafana Legacy Alerting: Removed in Grafana 11.0, must use Unified Alerting

## Open Questions

Things that couldn't be fully resolved:

1. **MassTransit IConnection sharing with health checks**
   - What we know: RabbitMQ recommends connection reuse; MassTransit creates its own connections internally
   - What's unclear: Whether AspNetCore.HealthChecks.Rabbitmq can be configured to reuse MassTransit's connection pool
   - Recommendation: Use the `IConnectionFactory` registration pattern; if issues arise, consider custom health check that uses MassTransit's `IBusControl.CheckHealth()`

2. **Grafana environment variable interpolation in provisioned files**
   - What we know: Grafana supports `${VAR}` syntax in provisioned YAML
   - What's unclear: Whether all settings (especially webhook URLs) support interpolation in recent versions
   - Recommendation: Test with Docker Compose before committing; fallback to Grafana API provisioning if needed

3. **Alert latency under 60 seconds**
   - What we know: Requirements specify alerts within 60 seconds of critical errors
   - What's unclear: Whether Grafana's default evaluation interval (1m) meets this, or if application-level alerting is needed
   - Recommendation: Implement both: Grafana for aggregate alerting (error rate spikes), application-level for immediate critical errors

## Sources

### Primary (HIGH confidence)
- [Xabaril/AspNetCore.Diagnostics.HealthChecks](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks) - Official health check packages, version 9.0.0
- [NuGet: AspNetCore.HealthChecks.NpgSql](https://www.nuget.org/packages/AspNetCore.HealthChecks.NpgSql) - Version 9.0.0, .NET 8.0+ compatible
- [NuGet: AspNetCore.HealthChecks.Redis](https://www.nuget.org/packages/AspNetCore.HealthChecks.Redis) - Version 9.0.0
- [NuGet: AspNetCore.HealthChecks.Rabbitmq](https://www.nuget.org/packages/AspNetCore.HealthChecks.Rabbitmq) - Version 9.0.0
- [Grafana Alerting Provisioning](https://grafana.com/docs/grafana/latest/alerting/set-up/provision-alerting-resources/file-provisioning/) - YAML file provisioning
- [Grafana Discord Integration](https://grafana.com/docs/grafana/latest/alerting/configure-notifications/manage-contact-points/integrations/configure-discord/) - Contact point configuration
- [MailKit GitHub](https://github.com/jstedfast/MailKit) - Microsoft-recommended SMTP library, version 4.11.0
- [Kubernetes Probes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/) - Official probe documentation

### Secondary (MEDIUM confidence)
- [Andrew Lock: Kubernetes Probes with ASP.NET Core](https://andrewlock.net/deploying-asp-net-core-applications-to-kubernetes-part-6-adding-health-checks-with-liveness-readiness-and-startup-probes/) - Best practices article
- [Milan Jovanovic: Health Checks](https://www.milanjovanovic.tech/blog/health-checks-in-asp-net-core) - Implementation patterns
- [Grafana Provisioning Examples](https://github.com/grafana/provisioning-alerting-examples) - Official example repository

### Tertiary (LOW confidence)
- Serilog Discord sinks - Multiple community packages exist but aren't recommended for production alerting due to coupling concerns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - NuGet packages verified, versions confirmed, widely adopted
- Architecture: HIGH - Patterns based on existing codebase patterns and official documentation
- Pitfalls: MEDIUM - Based on community experience and documentation warnings
- Grafana provisioning: MEDIUM - Documentation verified but Docker Compose testing recommended

**Research date:** 2026-01-22
**Valid until:** 60 days (stable packages, established patterns)
