using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that ensures every request has correlation and trace IDs.
/// </summary>
public sealed class CorrelationMiddleware
{
    private static readonly Regex CorrelationPattern = new("^[A-Za-z0-9-]+$", RegexOptions.Compiled);
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string RequestIdHeader = "X-Request-Id";
    private const string TraceIdHeader = "X-Trace-Id";
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";
    private const string BaggageHeader = "baggage";

    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        var requestId = GetOrCreateRequestId(context);

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("correlation.id", correlationId);
            activity.SetTag("request.id", requestId);
            activity.SetBaggage("correlation.id", correlationId);
        }

        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;

        context.Response.Headers[CorrelationIdHeader] = correlationId;
        context.Response.Headers[RequestIdHeader] = requestId;

        if (activity is not null)
        {
            context.Response.Headers[TraceIdHeader] = activity.TraceId.ToString();
            if (!string.IsNullOrWhiteSpace(activity.Id))
            {
                context.Response.Headers[TraceParentHeader] = activity.Id;
            }

            if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
            {
                context.Response.Headers[TraceStateHeader] = activity.TraceStateString;
            }

            var baggage = string.Join(",", activity.Baggage.Select(item =>
                $"{WebUtility.UrlEncode(item.Key)}={WebUtility.UrlEncode(item.Value)}"));
            if (!string.IsNullOrWhiteSpace(baggage))
            {
                context.Response.Headers[BaggageHeader] = baggage;
            }
        }

        // Ensure downstream components see correlation headers too.
        if (!context.Request.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Request.Headers[CorrelationIdHeader] = correlationId;
        }

        if (!context.Request.Headers.ContainsKey(RequestIdHeader))
        {
            context.Request.Headers[RequestIdHeader] = requestId;
        }

        if (activity is not null && !string.IsNullOrWhiteSpace(activity.Id))
        {
            if (!context.Request.Headers.ContainsKey(TraceIdHeader))
            {
                context.Request.Headers[TraceIdHeader] = activity.TraceId.ToString();
            }

            if (!context.Request.Headers.ContainsKey(TraceParentHeader))
            {
                context.Request.Headers[TraceParentHeader] = activity.Id;
            }

            if (!string.IsNullOrWhiteSpace(activity.TraceStateString) &&
                !context.Request.Headers.ContainsKey(TraceStateHeader))
            {
                context.Request.Headers[TraceStateHeader] = activity.TraceStateString;
            }

            var baggageValue = string.Join(",", activity.Baggage.Select(item => $"{item.Key}={item.Value}"));
            if (!string.IsNullOrWhiteSpace(baggageValue) && !context.Request.Headers.ContainsKey(BaggageHeader))
            {
                context.Request.Headers[BaggageHeader] = baggageValue;
            }
        }

        await _next(context);
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var header) &&
            !string.IsNullOrWhiteSpace(header))
        {
            var candidate = header.ToString().Trim();
            if (candidate.Length <= 64 && CorrelationPattern.IsMatch(candidate))
            {
                return candidate;
            }
        }

        var activity = Activity.Current;
        if (activity is not null && activity.TraceId != default)
        {
            return activity.TraceId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string GetOrCreateRequestId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var header) &&
            !string.IsNullOrWhiteSpace(header))
        {
            var candidate = header.ToString().Trim();
            if (candidate.Length <= 64 && CorrelationPattern.IsMatch(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
