---
phase: 04-health-alerting
plan: 02
subsystem: notifications
tags: [mailkit, discord-webhook, smtp, alerts, throttling]

# Dependency graph
requires:
  - phase: 01-logging-foundation
    provides: Centralized logging infrastructure, log context enrichment
  - phase: 02-distributed-tracing
    provides: TraceId and CorrelationId for alert context
provides:
  - IAlertDispatcher service for sending alerts
  - Discord webhook client with color-coded severity embeds
  - SMTP email sender using MailKit with HTML templates
  - Alert throttling to prevent alert storms (configurable window)
affects: [04-03, 05-error-handling, gateway-integration, agent-alerting]

# Tech tracking
tech-stack:
  added: [MailKit 4.11.0]
  patterns: [alert-dispatcher, channel-abstraction, throttling]

key-files:
  created:
    - src/Dhadgar.Notifications/Alerting/AlertMessage.cs
    - src/Dhadgar.Notifications/Alerting/IAlertDispatcher.cs
    - src/Dhadgar.Notifications/Alerting/AlertDispatcher.cs
    - src/Dhadgar.Notifications/Alerting/AlertThrottler.cs
    - src/Dhadgar.Notifications/Discord/DiscordOptions.cs
    - src/Dhadgar.Notifications/Discord/IDiscordWebhook.cs
    - src/Dhadgar.Notifications/Discord/DiscordWebhookClient.cs
    - src/Dhadgar.Notifications/Email/EmailOptions.cs
    - src/Dhadgar.Notifications/Email/IEmailSender.cs
    - src/Dhadgar.Notifications/Email/SmtpEmailSender.cs
  modified:
    - Directory.Packages.props
    - src/Dhadgar.Notifications/Dhadgar.Notifications.csproj
    - src/Dhadgar.Notifications/appsettings.json
    - src/Dhadgar.Notifications/Program.cs

key-decisions:
  - "Use StringBuilder for HTML email body (raw string literals with CSS braces problematic)"
  - "Alert throttle key: ServiceName + Title + ExceptionType (deduplication granularity)"
  - "Dispatch to Discord and email in parallel (no dependency between channels)"
  - "Graceful degradation when Discord/email not configured (log debug, no exception)"

patterns-established:
  - "AlertMessage record: Standard alert payload with TraceId, CorrelationId, ExceptionType"
  - "AlertThrottler: Sliding window throttle with automatic cleanup"
  - "IDiscordWebhook/IEmailSender: Channel abstractions for testability"

# Metrics
duration: 12min
completed: 2026-01-22
---

# Phase 4 Plan 2: Alerting Infrastructure Summary

**Discord webhook and SMTP email alerting with throttling via MailKit, supporting severity-colored embeds and HTML emails with trace context**

## Performance

- **Duration:** 12 min
- **Started:** 2026-01-22T00:00:00Z
- **Completed:** 2026-01-22T00:12:00Z
- **Tasks:** 3
- **Files modified:** 14

## Accomplishments
- AlertMessage record with severity enum, trace context, and additional data support
- Discord webhook client with color-coded embeds (red=critical, orange=error, yellow=warning)
- MailKit-based SMTP sender with HTML email templates showing severity-colored headers
- Alert throttler with configurable sliding window (default 5 minutes)
- Full DI wiring in Notifications service with configurable options

## Task Commits

Each task was committed atomically:

1. **Task 1: Add MailKit package and create alert message types** - `a36a69e` (feat)
2. **Task 2: Create Discord webhook and email sender implementations** - `00883d0` (feat)
3. **Task 3: Create throttled alert dispatcher and wire DI** - `a8fd519` (feat)

## Files Created/Modified
- `Directory.Packages.props` - Added MailKit 4.11.0 package version
- `src/Dhadgar.Notifications/Dhadgar.Notifications.csproj` - Added MailKit package reference
- `src/Dhadgar.Notifications/Alerting/AlertMessage.cs` - Alert payload record with severity enum
- `src/Dhadgar.Notifications/Alerting/IAlertDispatcher.cs` - Alert dispatch interface
- `src/Dhadgar.Notifications/Alerting/AlertDispatcher.cs` - Multi-channel dispatcher with throttling
- `src/Dhadgar.Notifications/Alerting/AlertThrottler.cs` - Sliding window throttle with cleanup
- `src/Dhadgar.Notifications/Discord/DiscordOptions.cs` - Discord webhook configuration
- `src/Dhadgar.Notifications/Discord/IDiscordWebhook.cs` - Discord webhook interface
- `src/Dhadgar.Notifications/Discord/DiscordWebhookClient.cs` - HTTP client for Discord webhook API
- `src/Dhadgar.Notifications/Email/EmailOptions.cs` - SMTP configuration options
- `src/Dhadgar.Notifications/Email/IEmailSender.cs` - Email sender interface
- `src/Dhadgar.Notifications/Email/SmtpEmailSender.cs` - MailKit SMTP implementation
- `src/Dhadgar.Notifications/appsettings.json` - Added Discord, Email, Alerting config sections
- `src/Dhadgar.Notifications/Program.cs` - DI registration for all alerting services

## Decisions Made
- **StringBuilder for HTML body**: Raw string literals with `$"""..."""` had issues with CSS braces (`{}`). StringBuilder provides cleaner approach with explicit CultureInfo.InvariantCulture for locale safety.
- **Throttle key granularity**: Using `ServiceName:Title:ExceptionType` allows similar errors from the same service to be grouped while still distinguishing different error types.
- **Parallel channel dispatch**: Discord and email have no dependency, so dispatch simultaneously using Task.WhenAll for better performance.
- **Graceful degradation**: When webhook URL or recipients not configured, implementations log at Debug level and return successfully (no exception thrown).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed raw string literal interpolation with CSS braces**
- **Found during:** Task 2 (SmtpEmailSender implementation)
- **Issue:** `$"""..."""` raw string literal with CSS `{ }` braces conflicted with interpolation
- **Fix:** Switched to StringBuilder with AppendLine and CultureInfo.InvariantCulture
- **Files modified:** src/Dhadgar.Notifications/Email/SmtpEmailSender.cs
- **Verification:** Build succeeds with 0 warnings
- **Committed in:** 00883d0 (Task 2 commit)

**2. [Rule 2 - Missing Critical] Added using statements for IDisposable resources**
- **Found during:** Task 2 (Discord and Email implementations)
- **Issue:** CA2000 warnings for MimeMessage and StringContent not being disposed
- **Fix:** Added `using` keyword for proper disposal
- **Files modified:** SmtpEmailSender.cs, DiscordWebhookClient.cs
- **Verification:** Build succeeds with 0 warnings
- **Committed in:** 00883d0 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 missing critical)
**Impact on plan:** All auto-fixes necessary for correctness. No scope creep.

## Issues Encountered
None - plan executed as specified after auto-fixes.

## User Setup Required

**External services require manual configuration.** To use alerting:

**Discord webhook:**
1. Create webhook in Discord channel settings
2. Set `Discord:WebhookUrl` in appsettings.json or user-secrets

**SMTP email:**
1. Configure SMTP server settings in `Email` section
2. Set `Email:AlertRecipients` to comma-separated email addresses

**Verification:**
- Service starts without errors (Discord/email gracefully degrade when not configured)
- When configured, alerts appear in Discord channel and email inbox

## Next Phase Readiness
- Alerting infrastructure complete and ready for integration
- Plan 04-03 can wire critical error handler to call IAlertDispatcher
- All channel implementations tested for graceful degradation
- No blockers for next phase

---
*Phase: 04-health-alerting*
*Completed: 2026-01-22*
