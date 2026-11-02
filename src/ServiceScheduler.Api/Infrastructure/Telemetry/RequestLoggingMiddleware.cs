using System.Diagnostics;
using ServiceScheduler.Api.Infrastructure.Errors;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Emits exactly one structured completion event per non-health request. The event deliberately
/// records the route template and bounded outcome vocabulary instead of the raw path, query string,
/// headers, bodies, or entity identifiers. It is registered before the exception handler so it sees
/// the final status produced by Problem Details handling.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private const string NoneOutcome = "none";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsHealthRequest(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var routeTemplate = RouteTemplateResolver.GetOrStore(context);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // A client disconnect is not an application fault. The client is gone, so this path
            // records the bounded client_cancelled outcome instead of a 5xx/internal_error.
            var statusCode = StatusCodes.Status499ClientClosedRequest;
            LogCompletion(
                LogLevel.Information,
                null,
                context.Request.Method,
                routeTemplate,
                statusCode,
                ProblemCodes.ClientCancelled,
                Stopwatch.GetElapsedTime(startedAt));
            ServiceSchedulerTelemetry.RecordHttpRequest(
                routeTemplate,
                context.Request.Method,
                ServiceSchedulerTelemetry.StatusClass(statusCode),
                ProblemCodes.ClientCancelled);
            throw;
        }
        catch (Exception exception)
        {
            var statusCode = context.Response.HasStarted
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;
            LogCompletion(
                LogLevel.Error,
                exception,
                context.Request.Method,
                routeTemplate,
                statusCode,
                ProblemCodes.InternalError,
                Stopwatch.GetElapsedTime(startedAt));
            ServiceSchedulerTelemetry.RecordHttpRequest(
                routeTemplate,
                context.Request.Method,
                ServiceSchedulerTelemetry.StatusClass(statusCode),
                ProblemCodes.InternalError);
            throw;
        }

        var finalStatusCode = context.Response.StatusCode;
        var finalOutcome = ResolveOutcome(context, finalStatusCode);
        LogCompletion(
            LevelFor(finalStatusCode),
            null,
            context.Request.Method,
            routeTemplate,
            finalStatusCode,
            finalOutcome,
            Stopwatch.GetElapsedTime(startedAt));
        ServiceSchedulerTelemetry.RecordHttpRequest(
            routeTemplate,
            context.Request.Method,
            ServiceSchedulerTelemetry.StatusClass(finalStatusCode),
            finalOutcome);
    }

    private void LogCompletion(
        LogLevel level,
        Exception? exception,
        string method,
        string routeTemplate,
        int statusCode,
        string outcome,
        TimeSpan elapsed)
    {
        logger.Log(
            level,
            exception,
            "EventName {EventName} RequestMethod {RequestMethod} RouteTemplate {RouteTemplate} StatusCode {StatusCode} StatusClass {StatusClass} Outcome {Outcome} ElapsedMilliseconds {ElapsedMilliseconds}",
            "RequestCompleted",
            method,
            routeTemplate,
            statusCode,
            ServiceSchedulerTelemetry.StatusClass(statusCode),
            outcome,
            (long)elapsed.TotalMilliseconds);
    }

    private static string ResolveOutcome(HttpContext context, int statusCode)
    {
        if (context.Items.TryGetValue(ProblemCodes.HttpContextItemKey, out var value)
            && value is string code
            && !string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        return ProblemCodes.ForStatus(statusCode) ?? NoneOutcome;
    }

    private static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static LogLevel LevelFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status499ClientClosedRequest => LogLevel.Information,
        >= StatusCodes.Status500InternalServerError => LogLevel.Error,
        >= StatusCodes.Status400BadRequest => LogLevel.Warning,
        _ => LogLevel.Information,
    };
}
