using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>Builds the documented POST body with the stable synthetic identifiers.</summary>
public static class AppointmentRequests
{
    public static Dictionary<string, object?> ForSaigon(string startsAt) =>
        Create(
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            startsAt);

    public static Dictionary<string, object?> ForLondon(string startsAt) =>
        Create(
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.LondonAnnualService,
            startsAt);

    public static Dictionary<string, object?> Create(
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        string startsAt) =>
        new(StringComparer.Ordinal)
        {
            ["customerId"] = customerId,
            ["vehicleId"] = vehicleId,
            ["serviceTypeId"] = serviceTypeId,
            ["startsAt"] = startsAt,
        };
}
