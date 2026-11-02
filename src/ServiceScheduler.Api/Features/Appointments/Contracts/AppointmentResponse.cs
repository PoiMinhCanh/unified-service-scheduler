namespace ServiceScheduler.Api.Features.Appointments.Contracts;

/// <summary>
/// Complete persisted appointment. Both create and retrieve return this shape; timestamps are UTC
/// values serialized with a trailing <c>Z</c>, and duration/end are stored snapshots.
/// </summary>
public sealed record AppointmentResponse(
    Guid Id,
    Guid DealershipId,
    Guid CustomerId,
    Guid VehicleId,
    Guid ServiceTypeId,
    Guid TechnicianId,
    Guid ServiceBayId,
    DateTime StartsAt,
    DateTime EndsAt,
    int DurationMinutes,
    string Status,
    DateTime CreatedAt);
