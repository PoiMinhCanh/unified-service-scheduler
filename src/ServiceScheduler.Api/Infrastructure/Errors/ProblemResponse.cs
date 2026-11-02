using Microsoft.AspNetCore.Mvc;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Infrastructure.Errors;

/// <summary>
/// Builds controller Problem Details responses with the same <c>code</c> and <c>traceId</c>
/// extensions used by middleware-generated responses.
/// </summary>
public static class ProblemResponse
{
    public static ObjectResult Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
        };
        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["traceId"] = ProblemTraceId.Resolve(httpContext.TraceIdentifier);
        httpContext.Items[ProblemCodes.HttpContextItemKey] = code;

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
