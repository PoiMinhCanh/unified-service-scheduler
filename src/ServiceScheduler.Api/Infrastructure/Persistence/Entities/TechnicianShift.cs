namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class TechnicianShift
{
    public Guid Id { get; set; }

    public Guid TechnicianId { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }
}
