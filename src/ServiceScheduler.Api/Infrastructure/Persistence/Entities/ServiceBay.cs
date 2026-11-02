namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class ServiceBay
{
    public Guid Id { get; set; }

    public Guid DealershipId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
