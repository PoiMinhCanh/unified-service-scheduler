namespace ServiceScheduler.Api.Features.Dealerships.Contracts;

/// <summary>Discovery response for one dealership.</summary>
public sealed record DealershipResponse(Guid Id, string Name, string TimeZoneId);
