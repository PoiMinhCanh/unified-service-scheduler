namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class Dealership
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = string.Empty;
}
