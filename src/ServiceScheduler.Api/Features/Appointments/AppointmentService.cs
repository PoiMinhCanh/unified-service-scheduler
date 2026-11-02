using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Features.Availability;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Features.Appointments;

/// <summary>
/// Performs the authoritative API precheck and then delegates atomic allocation to the SQL
/// procedure boundary. Booking never reads the service-catalog cache; the procedure revalidates
/// every predicate before committing.
/// </summary>
public sealed class AppointmentService(
    SchedulerDbContext dbContext,
    BookingAllocator allocator,
    TimeProvider timeProvider,
    IOptions<BookingOptions> options,
    ILogger<AppointmentService> logger)
{
    public async Task<AppointmentCreationResult> CreateAsync(
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        DateTimeOffset startsAtUtc,
        CancellationToken cancellationToken)
    {
        var budgetStartedAtUtc = timeProvider.GetUtcNow();
        var evaluatedAtUtc = budgetStartedAtUtc;
        var startUtc = startsAtUtc.ToUniversalTime();

        var serviceLookup = await AvailabilityQueries
            .ServiceLookups(dbContext, dealershipId, serviceTypeId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (serviceLookup is null)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.NotFound);
        }

        if (IsBudgetExhausted(budgetStartedAtUtc))
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.DependencyUnavailable);
        }

        var customer = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.NotFound);
        }

        var vehicle = await dbContext.Vehicles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == vehicleId, cancellationToken)
            .ConfigureAwait(false);

        if (vehicle is null)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.NotFound);
        }

        if (vehicle.CustomerId != customerId)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.ValidationError);
        }

        if (!AvailabilityInterval.TryCreate(
                startUtc,
                evaluatedAtUtc,
                serviceLookup.DurationMinutes,
                out var endsAtUtc))
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.ValidationError);
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(serviceLookup.TimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            logger.LogError(
                exception,
                "EventName {EventName} ConfigurationFailure {ConfigurationFailure}; a dealership has an unknown persisted IANA time zone.",
                "BookingConfigurationError",
                "unknown_time_zone");
            return new AppointmentCreationResult(AppointmentCreationStatus.InternalError);
        }
        catch (InvalidTimeZoneException exception)
        {
            logger.LogError(
                exception,
                "EventName {EventName} ConfigurationFailure {ConfigurationFailure}; a dealership has an invalid persisted IANA time zone.",
                "BookingConfigurationError",
                "invalid_time_zone");
            return new AppointmentCreationResult(AppointmentCreationStatus.InternalError);
        }

        if (!OpeningCoverage.TryGetLocalInterval(
                startUtc,
                endsAtUtc,
                timeZone,
                out var localStart,
                out var localEnd))
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.CapacityUnavailable);
        }

        var localDay = TimeZoneInfo.ConvertTime(startUtc, timeZone).DayOfWeek;
        var withinOpeningHours = await AvailabilityQueries
            .OpeningStarts(dbContext, dealershipId, (byte)localDay, localStart, localEnd)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!withinOpeningHours)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.CapacityUnavailable);
        }

        if (IsBudgetExhausted(budgetStartedAtUtc))
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.DependencyUnavailable);
        }

        var technicianCandidates = await AvailabilityQueries
            .TechnicianCandidates(dbContext, dealershipId, serviceTypeId, startUtc, endsAtUtc)
            .Take(options.Value.MaxCandidateAttempts)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var technicianIds = technicianCandidates.Distinct().ToList();

        var bayIds = await AvailabilityQueries
            .BayCandidates(dbContext, dealershipId, startUtc, endsAtUtc)
            .Take(options.Value.MaxCandidateAttempts)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (technicianIds.Count == 0 || bayIds.Count == 0)
        {
            return new AppointmentCreationResult(AppointmentCreationStatus.CapacityUnavailable);
        }

        var allocation = await allocator.AllocateAsync(
            dealershipId,
            customerId,
            vehicleId,
            serviceTypeId,
            startUtc,
            technicianIds,
            bayIds,
            budgetStartedAtUtc,
            cancellationToken).ConfigureAwait(false);

        if (allocation.Status != BookingAllocationStatus.Succeeded)
        {
            return new AppointmentCreationResult(allocation.Status switch
            {
                BookingAllocationStatus.ValidationError => AppointmentCreationStatus.ValidationError,
                BookingAllocationStatus.CapacityUnavailable => AppointmentCreationStatus.CapacityUnavailable,
                BookingAllocationStatus.DependencyUnavailable => AppointmentCreationStatus.DependencyUnavailable,
                BookingAllocationStatus.InternalError => AppointmentCreationStatus.InternalError,
                _ => AppointmentCreationStatus.InternalError,
            });
        }

        if (allocation.Procedure is null)
        {
            logger.LogError("Booking allocation reported success without a procedure result.");
            return new AppointmentCreationResult(AppointmentCreationStatus.InternalError);
        }

        return new AppointmentCreationResult(
            AppointmentCreationStatus.Created,
            new CreatedAppointment(
                allocation.Procedure.AppointmentId,
                dealershipId,
                customerId,
                vehicleId,
                serviceTypeId,
                allocation.TechnicianId,
                allocation.ServiceBayId,
                startUtc,
                allocation.Procedure.EndUtc,
                allocation.Procedure.DurationMinutes,
                AppointmentStatuses.Confirmed,
                allocation.Procedure.CreatedUtc));
    }

    public async Task<CreatedAppointment?> GetAsync(
        Guid dealershipId,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.Id == appointmentId
                && appointment.DealershipId == dealershipId)
            .Select(appointment => new CreatedAppointment(
                appointment.Id,
                appointment.DealershipId,
                appointment.CustomerId,
                appointment.VehicleId,
                appointment.ServiceTypeId,
                appointment.TechnicianId,
                appointment.ServiceBayId,
                appointment.StartUtc,
                appointment.EndUtc,
                appointment.DurationMinutes,
                appointment.Status,
                appointment.CreatedUtc))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsBudgetExhausted(DateTimeOffset budgetStartedAtUtc) =>
        timeProvider.GetUtcNow() - budgetStartedAtUtc
        >= TimeSpan.FromSeconds(options.Value.RequestBudgetSeconds);
}
