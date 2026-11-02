namespace ServiceScheduler.Api.Features.Availability.Contracts;

/// <summary>
/// Advisory availability result. Timestamps are UTC values serialized with a trailing <c>Z</c>.
/// Technician and bay identifiers are deliberately absent.
/// </summary>
public sealed record AvailabilityResponse(
    Guid DealershipId,
    Guid ServiceTypeId,
    bool Available,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime EvaluatedAt);
