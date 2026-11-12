using RatCache.Web.Controllers;

namespace RatCache.Web.Services;

// NFR-4.4: every response carries an X-Request-Id header, and every unhandled server error is
// logged with that same ID (plus a timestamp from the logger, see appsettings.json), so a user
// can quote the ID and a developer can find the log line.
// NFR-4.3: an unhandled exception on /api or /raw returns a generic error with no stack trace
// or internal details. Web pages keep using the exception handler in Program.cs (/Error page).
// NFR-1.4: only the method, path and request ID are logged here; never bodies, queries or headers.
public sealed class RequestIdAndErrorMiddleware(RequestDelegate next, ILogger<RequestIdAndErrorMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Same value the framework puts in its own log scopes as "RequestId".
        var requestId = ctx.TraceIdentifier;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        try
        {
            await next(ctx);
        }
        catch (Exception ex) when (IsApiOrRaw(ctx.Request)
                                   && !ctx.Response.HasStarted
                                   && !ctx.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path} (request {RequestId})",
                ctx.Request.Method, SafePath(ctx.Request), requestId);
            await WriteServerErrorAsync(ctx, requestId);
        }
    }

    private static bool IsApiOrRaw(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/raw");

    // The path comes from the client, so strip line breaks to keep it from forging log lines.
    private static string SafePath(HttpRequest request) =>
        (request.Path.Value ?? "").Replace("\r", "").Replace("\n", "");

    private static async Task WriteServerErrorAsync(HttpContext ctx, string requestId)
    {
        if (ctx.Request.Path.StartsWithSegments("/raw"))
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync($"server error (request {requestId})\n");
            return;
        }

        await ApiErrors.WriteAsync(ctx.Response, StatusCodes.Status500InternalServerError, "server_error",
            $"Something went wrong on our end. If it keeps happening, report request ID {requestId}.");
    }
}