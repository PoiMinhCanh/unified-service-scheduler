using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Infrastructure.Errors;

/// <summary>
/// Converts only classified SQL dependency failures into a sanitized 503. All other exceptions
/// remain sanitized 500 responses handled by the default Problem Details pipeline.
/// </summary>
public sealed class SqlDependencyExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<SqlDependencyExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        if (!SqlFailureClassifier.IsUnavailable(exception))
        {
            return false;
        }

        var traceId = ProblemTraceId.Resolve(httpContext.TraceIdentifier);
        logger.LogWarning(
            "EventName {EventName} RequestMethod {RequestMethod} RouteTemplate {RouteTemplate} StatusCode {StatusCode}; SQL dependency unavailable, returning 503.",
            "DependencyUnavailable",
            httpContext.Request.Method,
            RouteTemplateResolver.GetOrStore(httpContext),
            StatusCodes.Status503ServiceUnavailable);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "The service is temporarily unavailable.",
            Detail = "A required dependency is unavailable. Retry the request later.",
        };
        problemDetails.Extensions["code"] = ProblemCodes.DependencyUnavailable;
        problemDetails.Extensions["traceId"] = traceId;
        httpContext.Items[ProblemCodes.HttpContextItemKey] = ProblemCodes.DependencyUnavailable;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        }).ConfigureAwait(false);
    }
}
