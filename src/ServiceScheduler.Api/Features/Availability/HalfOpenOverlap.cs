namespace ServiceScheduler.Api.Features.Availability;

/// <summary>
/// Half-open interval overlap rule and specification mirror of the conflict predicates in
/// <see cref="AvailabilityQueries.TechnicianCandidates"/> and
/// <see cref="AvailabilityQueries.BayCandidates"/>. SQL Server remains authoritative; keep this rule
/// and the generated SQL predicates in sync and verify both.
/// </summary>
public static class HalfOpenOverlap
{
    public static bool Overlaps(
        DateTimeOffset existingStartUtc,
        DateTimeOffset existingEndUtc,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc) =>
        existingStartUtc < requestedEndUtc && existingEndUtc > requestedStartUtc;
}
