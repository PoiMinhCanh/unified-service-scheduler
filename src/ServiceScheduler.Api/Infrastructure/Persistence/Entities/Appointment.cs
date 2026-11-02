namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class Appointment
{
    public Guid Id { get; set; }

    public Guid DealershipId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid VehicleId { get; set; }

    public Guid ServiceTypeId { get; set; }

    public Guid TechnicianId { get; set; }

    public Guid ServiceBayId { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    /// <summary>Snapshot of the configured service duration at confirmation time.</summary>
    public int DurationMinutes { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
