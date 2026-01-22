using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Combined options for audit infrastructure configuration.
/// </summary>
public sealed class AuditOptions
{
    /// <summary>
    /// Options for the audit queue.
    /// </summary>
    public AuditQueueOptions Queue { get; } = new();

    /// <summary>
    /// Options for the audit writer service.
    /// </summary>
    public AuditWriterOptions Writer { get; } = new();

    /// <summary>
    /// Options for the audit cleanup service.
    /// </summary>
    public AuditCleanupOptions Cleanup { get; } = new();
}

/// <summary>
/// Extension methods for registering audit infrastructure services.
/// </summary>
public static class AuditExtensions
{
    /// <summary>
    /// Adds the audit infrastructure services to the service collection.
    /// </summary>
    /// <typeparam name="TContext">
    /// The DbContext type that implements <see cref="IAuditDbContext"/>.
    /// This context must have a <c>DbSet&lt;ApiAuditRecord&gt;</c> property.
    /// </typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// <list type="bullet">
    ///   <item><see cref="IAuditQueue"/> as singleton (channel-based queue)</item>
    ///   <item><see cref="AuditWriterService{TContext}"/> as hosted service (batch writer)</item>
    ///   <item><see cref="AuditCleanupService{TContext}"/> as hosted service (90-day retention)</item>
    /// </list>
    /// </para>
    /// <para>
    /// After calling this method, use <see cref="UseAuditMiddleware"/> on the application
    /// to register the audit middleware (after authentication middleware).
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // In Program.cs
    /// builder.Services.AddAuditInfrastructure&lt;MyDbContext&gt;(options =>
    /// {
    ///     options.Queue.Capacity = 20_000;
    ///     options.Writer.BatchSize = 200;
    ///     options.Cleanup.RetentionPeriod = TimeSpan.FromDays(60);
    /// });
    ///
    /// var app = builder.Build();
    ///
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseAuditMiddleware(); // After auth middleware
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAuditInfrastructure<TContext>(
        this IServiceCollection services,
        Action<AuditOptions>? configure = null)
        where TContext : DbContext, IAuditDbContext
    {
        // Create combined options and apply configuration
        var options = new AuditOptions();
        configure?.Invoke(options);

        // Register individual option classes
        services.Configure<AuditQueueOptions>(o =>
        {
            o.Capacity = options.Queue.Capacity;
        });

        services.Configure<AuditWriterOptions>(o =>
        {
            o.BatchSize = options.Writer.BatchSize;
            o.FlushInterval = options.Writer.FlushInterval;
        });

        services.Configure<AuditCleanupOptions>(o =>
        {
            o.Interval = options.Cleanup.Interval;
            o.RetentionPeriod = options.Cleanup.RetentionPeriod;
            o.BatchSize = options.Cleanup.BatchSize;
            o.Enabled = options.Cleanup.Enabled;
        });

        // Register audit queue as singleton (shared across all requests)
        services.AddSingleton<IAuditQueue, AuditQueue>();

        // Register background services
        services.AddHostedService<AuditWriterService<TContext>>();
        services.AddHostedService<AuditCleanupService<TContext>>();

        return services;
    }

    /// <summary>
    /// Adds the audit middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>IMPORTANT:</b> This middleware MUST be registered AFTER authentication middleware.
    /// The middleware checks <c>context.User.Identity.IsAuthenticated</c> to determine
    /// whether to audit the request.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseAuditMiddleware(); // After auth
    /// app.MapControllers();
    /// </code>
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseAuditMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditMiddleware>();
    }
}
