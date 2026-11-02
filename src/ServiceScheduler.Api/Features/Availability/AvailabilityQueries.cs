using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Features.Availability;

/// <summary>Dealership-scoped service duration and IANA time-zone identifier.</summary>
public sealed record ServiceLookup(int DurationMinutes, string TimeZoneId);

/// <summary>
/// Query shapes used by availability. They keep dealership scope, full-interval coverage, and
/// half-open conflict filtering in SQL Server and order candidates deterministically by name and ID.
/// <see cref="ShiftCoverage"/>, <see cref="HalfOpenOverlap"/>, and <see cref="OpeningCoverage"/> are
/// unit-tested specification mirrors of these predicates; SQL remains authoritative.
/// </summary>
public static class AvailabilityQueries
{
    public static IQueryable<ServiceLookup> ServiceLookups(
        SchedulerDbContext dbContext,
        Guid dealershipId,
        Guid serviceTypeId)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return from serviceType in dbContext.ServiceTypes.AsNoTracking()
               join dealership in dbContext.Dealerships.AsNoTracking()
                   on serviceType.DealershipId equals dealership.Id
               where serviceType.Id == serviceTypeId
                     && serviceType.DealershipId == dealershipId
               select new ServiceLookup(serviceType.DurationMinutes, dealership.TimeZoneId);
    }

    public static IQueryable<TimeOnly> OpeningStarts(
        SchedulerDbContext dbContext,
        Guid dealershipId,
        byte dayOfWeek,
        TimeOnly localStart,
        TimeOnly localEnd)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.DealershipOpeningHours
            .AsNoTracking()
            .Where(hours => hours.DealershipId == dealershipId
                            && hours.DayOfWeek == dayOfWeek
                            && hours.OpensAt <= localStart
                            && hours.ClosesAt >= localEnd)
            .Select(hours => hours.OpensAt);
    }

    public static IQueryable<Guid> TechnicianCandidates(
        SchedulerDbContext dbContext,
        Guid dealershipId,
        Guid serviceTypeId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return from technician in dbContext.Technicians.AsNoTracking()
               join qualification in dbContext.TechnicianQualifications.AsNoTracking()
                   on new { TechnicianId = technician.Id, technician.DealershipId }
                   equals new { qualification.TechnicianId, qualification.DealershipId }
               join shift in dbContext.TechnicianShifts.AsNoTracking()
                   on technician.Id equals shift.TechnicianId
               where technician.DealershipId == dealershipId
                     && technician.IsActive
                     && qualification.ServiceTypeId == serviceTypeId
                     && shift.StartUtc <= startsAtUtc
                     && shift.EndUtc >= endsAtUtc
                     && !dbContext.Appointments.Any(appointment =>
                         appointment.DealershipId == dealershipId
                         && appointment.TechnicianId == technician.Id
                         && appointment.Status == AppointmentStatuses.Confirmed
                         && appointment.StartUtc < endsAtUtc
                         && appointment.EndUtc > startsAtUtc)
               orderby technician.Name, technician.Id
               select technician.Id;
    }

    public static IQueryable<Guid> BayCandidates(
        SchedulerDbContext dbContext,
        Guid dealershipId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.ServiceBays
            .AsNoTracking()
            .Where(bay => bay.DealershipId == dealershipId && bay.IsActive)
            .Where(bay => !dbContext.Appointments.Any(appointment =>
                appointment.DealershipId == dealershipId
                && appointment.ServiceBayId == bay.Id
                && appointment.Status == AppointmentStatuses.Confirmed
                && appointment.StartUtc < endsAtUtc
                && appointment.EndUtc > startsAtUtc))
            .OrderBy(bay => bay.Name)
            .ThenBy(bay => bay.Id)
            .Select(bay => bay.Id);
    }
}
