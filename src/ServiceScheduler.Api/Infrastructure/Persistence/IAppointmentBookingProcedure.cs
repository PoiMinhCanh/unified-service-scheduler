namespace ServiceScheduler.Api.Infrastructure.Persistence;

public enum BookingProcedureStatus
{
    Succeeded,
    TechnicianConflict,
    BayConflict,
    InvalidRequest,
    ResourceNotEligible,
    CoverageUnavailable,
    TimeZoneMappingInvalid,
    Deadlock,
    LockTimeout,
    CommandTimeout,
    DependencyUnavailable,
}

public sealed record BookingProcedureRequest(
    Guid DealershipId,
    Guid CustomerId,
    Guid VehicleId,
    Guid ServiceTypeId,
    Guid TechnicianId,
    Guid ServiceBayId,
    DateTimeOffset StartUtc,
    DateTimeOffset EvaluatedAtUtc);

public sealed record BookingProcedureResult(
    BookingProcedureStatus Status,
    Guid AppointmentId = default,
    DateTimeOffset EndUtc = default,
    int DurationMinutes = 0,
    DateTimeOffset CreatedUtc = default);

public interface IAppointmentBookingProcedure
{
    Task<BookingProcedureResult> ExecuteAsync(
        BookingProcedureRequest request,
        CancellationToken cancellationToken);
}
