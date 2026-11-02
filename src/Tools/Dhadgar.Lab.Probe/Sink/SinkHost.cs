using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Dhadgar.Lab.Tests")]

namespace Dhadgar.Lab.Probe;

/// <summary>
/// The HTTP front of the lab's webhook and email sink (L36). Every POST is recorded by <see cref="SinkRecorder"/>; the
/// reader routes return the recorded requests and clear them. The sink listens on container port 8080.
/// </summary>
public static class SinkHost
{
    /// <summary>The URL the sink binds inside the container (the lab maps it to host port 18090).</summary>
    public const string ListenUrl = "http://+:8080";

    /// <summary>The reader route: <c>GET</c> returns requests with <c>seq</c> above <c>since</c>, <c>DELETE</c> clears them.</summary>
    public const string RequestsRoute = "/_sink/requests";

    /// <summary>
    /// Builds the sink application over <paramref name="recorder"/>. Tests call this with a loopback URL; the probe mode
    /// calls <see cref="Run"/>.
    /// </summary>
    public static WebApplication Build(SinkRecorder recorder, string url)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(url);
        WebApplication app = builder.Build();

        app.MapGet(RequestsRoute, (long? since) =>
        {
            if (since is < 0)
            {
                return Results.BadRequest("since must be zero or more");
            }

            return Results.Json(recorder.Since(since ?? 0));
        });

        app.MapDelete(RequestsRoute, () =>
        {
            recorder.Clear();
            return Results.NoContent();
        });

        app.MapPost("/{**path}", async (HttpContext ctx) =>
        {
            string path = ctx.Request.Path.Value ?? "/";
            var headers = ctx.Request.Headers.Select(h => new KeyValuePair<string, string>(
                h.Key, string.Join(",", h.Value.ToArray())));
            string body = await ReadBoundedAsync(ctx.Request.Body, ctx.RequestAborted).ConfigureAwait(false);
            recorder.Record(path, headers, body);
            return Results.Ok();
        });

        return app;
    }

    /// <summary>
    /// Reads at most <see cref="SinkRecorder.MaxBodyChars"/> characters of <paramref name="body"/>. The rest is never read,
    /// so a larger body is never buffered, and the recorder stores the cut.
    /// </summary>
    internal static async Task<string> ReadBoundedAsync(Stream body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var reader = new StreamReader(body);
        char[] buffer = new char[SinkRecorder.MaxBodyChars];
        try
        {
            int read = await reader.ReadBlockAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            return new string(buffer, 0, read);
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    /// <summary>
    /// Runs the sink on <see cref="ListenUrl"/> until the process stops. Returns 0 on a clean stop, 2 when the host fails.
    /// </summary>
    public static int Run(TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        try
        {
            var recorder = new SinkRecorder(TimeProvider.System);
            using WebApplication app = Build(recorder, ListenUrl);
            app.Run();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            stderr.WriteLine($"sink failed to start: {ex.GetType().Name}");
            return 2;
        }
    }
}
