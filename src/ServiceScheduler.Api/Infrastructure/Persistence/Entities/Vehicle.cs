namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class Vehicle
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string Vin { get; set; } = string.Empty;

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}
