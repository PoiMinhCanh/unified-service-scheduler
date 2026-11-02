using Microsoft.AspNetCore.Diagnostics;

namespace ServiceScheduler.Api.Infrastructure.Errors;

/// <summary>
/// Marks a client-aborted cancellation as a closed request instead of an internal server error.
/// The client is already gone, so no Problem Details body is written; request-completion telemetry
/// records the bounded <c>client_cancelled</c> outcome with status 499.
/// </summary>
public sealed class ClientCancellationExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not OperationCanceledException
            || !httpContext.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        httpContext.Items[ProblemCodes.HttpContextItemKey] = ProblemCodes.ClientCancelled;
        return ValueTask.FromResult(true);
    }
}
