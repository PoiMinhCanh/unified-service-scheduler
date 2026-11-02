namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class TechnicianQualification
{
    public Guid TechnicianId { get; set; }

    public Guid ServiceTypeId { get; set; }

    public Guid DealershipId { get; set; }
}
