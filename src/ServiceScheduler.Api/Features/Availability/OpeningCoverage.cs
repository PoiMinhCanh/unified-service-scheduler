namespace ServiceScheduler.Api.Features.Availability;

/// <summary>
/// Dealership-local opening coverage. UTC instants are converted to wall-clock values; the interval
/// must stay on one local date and fit within a single configured opening row. Repeated fall-back
/// wall times compare identically, and a configured boundary inside a spring-forward gap is compared
/// as the stored wall-clock value.
/// </summary>
public static class OpeningCoverage
{
    public static bool TryGetLocalInterval(
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        TimeZoneInfo timeZone,
        out TimeOnly localStart,
        out TimeOnly localEnd)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        localStart = default;
        localEnd = default;

        var convertedStart = TimeZoneInfo.ConvertTime(startsAtUtc, timeZone);
        var convertedEnd = TimeZoneInfo.ConvertTime(endsAtUtc, timeZone);

        if (convertedStart.Date != convertedEnd.Date)
        {
            return false;
        }

        localStart = TimeOnly.FromDateTime(convertedStart.DateTime);
        localEnd = TimeOnly.FromDateTime(convertedEnd.DateTime);
        return true;
    }

    /// <summary>
    /// Specification mirror of the one-row coverage predicate in
    /// <see cref="AvailabilityQueries.OpeningStarts"/>. SQL Server remains authoritative; keep this
    /// rule and the generated SQL predicate in sync and verify both. An end exactly at closing and a
    /// start exactly at opening are allowed.
    /// </summary>
    public static bool IsWithin(
        TimeOnly localStart,
        TimeOnly localEnd,
        TimeOnly opensAt,
        TimeOnly closesAt) =>
        opensAt <= localStart && closesAt >= localEnd;
}
