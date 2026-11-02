namespace ServiceScheduler.Api.Infrastructure.Errors;

/// <summary>Stable application error codes published in Problem Details.</summary>
public static class ProblemCodes
{
    /// <summary>HttpContext.Items key used to carry the bounded Problem code into request telemetry.</summary>
    public const string HttpContextItemKey = "scheduler.problem.code";

    public const string ValidationError = "validation_error";

    public const string NotFound = "not_found";

    public const string CapacityUnavailable = "capacity_unavailable";

    public const string DependencyUnavailable = "dependency_unavailable";

    public const string InternalError = "internal_error";

    /// <summary>Request outcome for a client-aborted request; no Problem Details response is written.</summary>
    public const string ClientCancelled = "client_cancelled";

    /// <summary>Returns the documented code for a status, or null for statuses outside the contract.</summary>
    public static string? ForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ValidationError,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status409Conflict => CapacityUnavailable,
        StatusCodes.Status500InternalServerError => InternalError,
        StatusCodes.Status503ServiceUnavailable => DependencyUnavailable,
        _ => null,
    };
}
