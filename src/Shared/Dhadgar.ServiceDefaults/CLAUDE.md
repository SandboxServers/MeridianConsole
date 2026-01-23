# ServiceDefaults Library

Common service wiring, middleware, and observability.

## Tech Stack
- .NET Class Library
- OpenTelemetry

## Contents
- `CorrelationMiddleware` - Request/trace ID tracking
- `ProblemDetailsMiddleware` - RFC 7807 error responses
- `RequestLoggingMiddleware` - HTTP request/response logging
- OpenTelemetry configuration

## Usage
```csharp
builder.Services.AddServiceDefaults();
```

## Notes
- This is a **shared library** - referenced by all services
- Middleware is auto-registered via AddServiceDefaults()
