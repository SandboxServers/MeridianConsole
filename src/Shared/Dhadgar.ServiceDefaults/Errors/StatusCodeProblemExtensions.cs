using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// F357: routing answers an unsupported content type (415) or an unmapped method (405) from its own endpoint,
/// which neither throws nor writes a body, so <see cref="BindingFailureProblem"/> never sees it. This writes the
/// D-ML233 problem+json for those two statuses when nothing else has written a body.
/// </summary>
public static class StatusCodeProblemExtensions
{
    // Same settings as ProblemDetailsMiddleware, so type, title and status are camelCase and null detail is omitted.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The statuses this middleware answers. Everything else is left alone.</summary>
    public static bool Handles(int statusCode) => statusCode is StatusCodes.Status405MethodNotAllowed or StatusCodes.Status415UnsupportedMediaType;

    /// <summary>
    /// Answers a 405 or 415 that routing produced with no body as problem+json. Other statuses, and responses that
    /// already have a body, content type or content length, are left exactly as they are.
    /// </summary>
    public static IApplicationBuilder UseDhadgarStatusCodeProblems(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            var status = context.Response.StatusCode;
            if (!Handles(status))
            {
                return; // leaves the response exactly as it is
            }

            var time = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier;
            var traceId = Activity.Current?.TraceId.ToString() ?? correlationId;
            var (type, title) = ProblemTypeTable.Describe(status);
            var problem = new ProblemDetails { Type = type, Title = title, Status = status, Instance = context.Request.Path.ToString() };
            problem.Extensions["traceId"] = traceId;
            problem.Extensions["correlationId"] = correlationId;
            problem.Extensions["timestamp"] = time.GetUtcNow();

            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions), context.RequestAborted);
        });
}
