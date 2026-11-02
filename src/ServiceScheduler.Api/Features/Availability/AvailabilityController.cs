using Microsoft.AspNetCore.Mvc;
using ServiceScheduler.Api.Features.Availability.Contracts;
using ServiceScheduler.Api.Infrastructure.Errors;

namespace ServiceScheduler.Api.Features.Availability;

/// <summary>Evaluates an advisory availability request without creating a reservation.</summary>
[ApiController]
[Route("api/dealerships")]
public sealed class AvailabilityController(
    AvailabilityService availabilityService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("{dealershipId}/availability")]
    [ProducesResponseType(typeof(AvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AvailabilityResponse>> GetAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        string? startsAt,
        CancellationToken cancellationToken)
    {
        var evaluatedAt = timeProvider.GetUtcNow();

        // Missing non-nullable value-type query parameters bind as Guid.Empty instead of producing
        // a model-state error, so the required service identifier is validated explicitly.
        if (serviceTypeId == Guid.Empty)
        {
            return ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "serviceTypeId is required.",
                ProblemCodes.ValidationError);
        }

        if (!StartsAtParser.TryParse(startsAt, out var parsedStartsAt))
        {
            return ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "startsAt must be a valid timestamp with Z or an explicit offset.",
                ProblemCodes.ValidationError);
        }

        var startsAtUtc = parsedStartsAt.ToUniversalTime();
        if (startsAtUtc <= evaluatedAt)
        {
            return ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "startsAt must be in the future.",
                ProblemCodes.ValidationError);
        }

        var evaluation = await availabilityService
            .EvaluateAsync(dealershipId, serviceTypeId, startsAtUtc, evaluatedAt, cancellationToken)
            .ConfigureAwait(false);

        return evaluation.Status switch
        {
            AvailabilityStatus.NotFound => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                "The requested resource was not found.",
                "The dealership or service type was not found for this dealership.",
                ProblemCodes.NotFound),
            AvailabilityStatus.InvalidInterval => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "The computed service end is outside the supported timestamp range.",
                ProblemCodes.ValidationError),
            _ => Ok(new AvailabilityResponse(
                dealershipId,
                serviceTypeId,
                evaluation.Available,
                evaluation.StartsAt.UtcDateTime,
                evaluation.EndsAt.UtcDateTime,
                evaluatedAt.UtcDateTime)),
        };
    }
}
