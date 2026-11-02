namespace ServiceScheduler.Api.Features.Availability;

/// <summary>
/// Builds the requested UTC interval from the authoritative SQL service duration. The start must be
/// strictly in the future and the computed end must stay within the supported timestamp range.
/// </summary>
public static class AvailabilityInterval
{
    public static bool TryCreate(
        DateTimeOffset startsAtUtc,
        DateTimeOffset evaluatedAtUtc,
        int durationMinutes,
        out DateTimeOffset endsAtUtc)
    {
        endsAtUtc = default;

        if (durationMinutes <= 0 || startsAtUtc <= evaluatedAtUtc)
        {
            return false;
        }

        try
        {
            endsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
