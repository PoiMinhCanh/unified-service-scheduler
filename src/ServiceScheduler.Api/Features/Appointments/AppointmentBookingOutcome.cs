namespace ServiceScheduler.Api.Features.Appointments;

public enum AppointmentCreationStatus
{
    Created,
    ValidationError,
    NotFound,
    CapacityUnavailable,
    DependencyUnavailable,
    InternalError,
}

public sealed record AppointmentCreationResult(
    AppointmentCreationStatus Status,
    CreatedAppointment? Appointment = null);

public sealed record CreatedAppointment(
    Guid AppointmentId,
    Guid DealershipId,
    Guid CustomerId,
    Guid VehicleId,
    Guid ServiceTypeId,
    Guid TechnicianId,
    Guid ServiceBayId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int DurationMinutes,
    string Status,
    DateTimeOffset CreatedUtc);
