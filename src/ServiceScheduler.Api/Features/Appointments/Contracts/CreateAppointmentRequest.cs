using System.ComponentModel.DataAnnotations;

namespace ServiceScheduler.Api.Features.Appointments.Contracts;

/// <summary>
/// Booking request. Resource, duration, end time, and identifiers are server-selected; clients
/// provide only the customer, vehicle, service type, and explicit-offset start.
/// </summary>
public sealed class CreateAppointmentRequest
{
    [Required]
    public Guid? CustomerId { get; init; }

    [Required]
    public Guid? VehicleId { get; init; }

    [Required]
    public Guid? ServiceTypeId { get; init; }

    [Required]
    public string? StartsAt { get; init; }
}
