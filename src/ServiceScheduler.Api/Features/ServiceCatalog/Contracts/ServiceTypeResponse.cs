namespace ServiceScheduler.Api.Features.ServiceCatalog.Contracts;

/// <summary>Dealership-scoped service catalog item.</summary>
public sealed record ServiceTypeResponse(Guid Id, string Name, int DurationMinutes);
