namespace ServiceScheduler.Api.Features.Availability;

/// <summary>
/// Specification mirror of the single-shift predicate in
/// <see cref="AvailabilityQueries.TechnicianCandidates"/>. SQL Server remains authoritative; keep
/// this rule and the generated SQL predicate in sync and verify both.
/// </summary>
public static class ShiftCoverage
{
    public static bool IsCovered(
        DateTimeOffset shiftStartUtc,
        DateTimeOffset shiftEndUtc,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc) =>
        shiftStartUtc <= startsAtUtc && shiftEndUtc >= endsAtUtc;
}
