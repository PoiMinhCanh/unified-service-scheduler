using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Extends the synthetic shift horizon for active technicians. The clock is captured once, the
/// start is the next dealership-local calendar day, and only missing weekday 09:00-17:00 intervals
/// are added. Historical shifts are never deleted, so a rerun is idempotent.
/// </summary>
public sealed class FutureShiftSeeder(SchedulerDbContext dbContext, TimeProvider timeProvider)
{
    public const int HorizonDays = 28;

    public static TimeOnly ShiftStart { get; } = new(9, 0);

    public static TimeOnly ShiftEnd { get; } = new(17, 0);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        var activeTechnicians = await dbContext.Technicians
            .AsNoTracking()
            .Where(technician => technician.IsActive)
            .Join(
                dbContext.Dealerships.AsNoTracking(),
                technician => technician.DealershipId,
                dealership => dealership.Id,
                (technician, dealership) => new { technician.Id, dealership.TimeZoneId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeTechnicians.Count == 0)
        {
            return;
        }

        var technicianIds = activeTechnicians.Select(technician => technician.Id).ToArray();
        var existingShifts = await dbContext.TechnicianShifts
            .AsNoTracking()
            .Where(shift => technicianIds.Contains(shift.TechnicianId))
            .Select(shift => new { shift.TechnicianId, shift.StartUtc, shift.EndUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingIntervals = existingShifts
            .Select(shift => (shift.TechnicianId, shift.StartUtc, shift.EndUtc))
            .ToHashSet();

        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        var newShifts = new List<TechnicianShift>();

        foreach (var technician in activeTechnicians)
        {
            var zone = ResolveZone(zones, technician.TimeZoneId);
            var nextLocalDay = TimeZoneInfo.ConvertTime(nowUtc, zone).Date.AddDays(1);

            for (var dayOffset = 0; dayOffset < HorizonDays; dayOffset++)
            {
                var localDay = nextLocalDay.AddDays(dayOffset);
                if (localDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var startUtc = ToUtc(localDay, ShiftStart, zone);
                var endUtc = ToUtc(localDay, ShiftEnd, zone);

                if (existingIntervals.Add((technician.Id, startUtc, endUtc)))
                {
                    newShifts.Add(new TechnicianShift
                    {
                        Id = Guid.NewGuid(),
                        TechnicianId = technician.Id,
                        StartUtc = startUtc,
                        EndUtc = endUtc,
                    });
                }
            }
        }

        if (newShifts.Count == 0)
        {
            return;
        }

        dbContext.TechnicianShifts.AddRange(newShifts);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TimeZoneInfo ResolveZone(
        IDictionary<string, TimeZoneInfo> cache,
        string timeZoneId)
    {
        if (cache.TryGetValue(timeZoneId, out var cached))
        {
            return cached;
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            cache[timeZoneId] = zone;
            return zone;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Dealership time zone '{timeZoneId}' is not supported on this host.",
                exception);
        }
    }

    private static DateTimeOffset ToUtc(DateTime localDate, TimeOnly localTime, TimeZoneInfo zone)
    {
        var localDateTime = DateTime.SpecifyKind(localDate.Date + localTime.ToTimeSpan(), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(localDateTime))
        {
            throw new InvalidOperationException(
                $"Local time {localDateTime:O} is invalid in time zone '{zone.Id}'.");
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, zone);
        return new DateTimeOffset(utc);
    }
}
