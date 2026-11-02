using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

/// <summary>
/// Deterministic synthetic configuration data. Identifiers are stable so tests and future demo
/// scripts can reference seeded rows. No appointment is seeded; ticket 3 owns appointment writes.
/// </summary>
public static class SeedData
{
    public static class DealershipIds
    {
        public static readonly Guid Saigon = Guid.Parse("a1000000-0000-0000-0000-000000000001");
        public static readonly Guid London = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    }

    public static class CustomerIds
    {
        public static readonly Guid AnNguyen = Guid.Parse("c1000000-0000-0000-0000-000000000001");
        public static readonly Guid OliverSmith = Guid.Parse("c1000000-0000-0000-0000-000000000002");
    }

    public static class VehicleIds
    {
        public static readonly Guid AnNguyenVehicle = Guid.Parse("b1000000-0000-0000-0000-000000000001");
        public static readonly Guid OliverSmithVehicle = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    }

    public static class ServiceTypeIds
    {
        public static readonly Guid SaigonOilChange = Guid.Parse("51000000-0000-0000-0000-000000000001");
        public static readonly Guid SaigonBrakeService = Guid.Parse("51000000-0000-0000-0000-000000000002");
        public static readonly Guid LondonAnnualService = Guid.Parse("51000000-0000-0000-0000-000000000003");
    }

    public static class TechnicianIds
    {
        public static readonly Guid SaigonActive = Guid.Parse("71000000-0000-0000-0000-000000000001");
        public static readonly Guid SaigonInactive = Guid.Parse("71000000-0000-0000-0000-000000000002");
        public static readonly Guid LondonActive = Guid.Parse("71000000-0000-0000-0000-000000000003");
        public static readonly Guid LondonInactive = Guid.Parse("71000000-0000-0000-0000-000000000004");
    }

    public static class ServiceBayIds
    {
        public static readonly Guid SaigonActive = Guid.Parse("81000000-0000-0000-0000-000000000001");
        public static readonly Guid SaigonInactive = Guid.Parse("81000000-0000-0000-0000-000000000002");
        public static readonly Guid LondonActive = Guid.Parse("81000000-0000-0000-0000-000000000003");
        public static readonly Guid LondonInactive = Guid.Parse("81000000-0000-0000-0000-000000000004");
    }

    public static TimeZoneMapping[] TimeZoneMappings { get; } =
    [
        new()
        {
            IanaId = "Asia/Ho_Chi_Minh",
            WindowsId = "SE Asia Standard Time",
        },
        new()
        {
            IanaId = "Europe/London",
            WindowsId = "GMT Standard Time",
        },
    ];

    public static Dealership[] Dealerships { get; } =
    [
        new()
        {
            Id = DealershipIds.Saigon,
            Name = "Saigon Central Motors",
            TimeZoneId = "Asia/Ho_Chi_Minh",
        },
        new()
        {
            Id = DealershipIds.London,
            Name = "London Riverside Motors",
            TimeZoneId = "Europe/London",
        },
    ];

    public static DealershipOpeningHour[] OpeningHours { get; } =
        BuildWeekdayOpeningHours(DealershipIds.Saigon)
            .Concat(BuildWeekdayOpeningHours(DealershipIds.London))
            .ToArray();

    public static Customer[] Customers { get; } =
    [
        new()
        {
            Id = CustomerIds.AnNguyen,
            Name = "An Nguyen",
            Email = "an.nguyen@example.test",
        },
        new()
        {
            Id = CustomerIds.OliverSmith,
            Name = "Oliver Smith",
            Email = "oliver.smith@example.test",
        },
    ];

    public static Vehicle[] Vehicles { get; } =
    [
        new()
        {
            Id = VehicleIds.AnNguyenVehicle,
            CustomerId = CustomerIds.AnNguyen,
            Vin = "1HGCM82633A000001",
            Make = "Honda",
            Model = "Accord",
        },
        new()
        {
            Id = VehicleIds.OliverSmithVehicle,
            CustomerId = CustomerIds.OliverSmith,
            Vin = "1HGCM82633A000002",
            Make = "Ford",
            Model = "Focus",
        },
    ];

    public static ServiceType[] ServiceTypes { get; } =
    [
        new()
        {
            Id = ServiceTypeIds.SaigonOilChange,
            DealershipId = DealershipIds.Saigon,
            Name = "Oil Change",
            DurationMinutes = 60,
        },
        new()
        {
            Id = ServiceTypeIds.SaigonBrakeService,
            DealershipId = DealershipIds.Saigon,
            Name = "Brake Service",
            DurationMinutes = 120,
        },
        new()
        {
            Id = ServiceTypeIds.LondonAnnualService,
            DealershipId = DealershipIds.London,
            Name = "Annual Service",
            DurationMinutes = 90,
        },
    ];

    public static Technician[] Technicians { get; } =
    [
        new()
        {
            Id = TechnicianIds.SaigonActive,
            DealershipId = DealershipIds.Saigon,
            Name = "Minh Tran",
            IsActive = true,
        },
        new()
        {
            Id = TechnicianIds.SaigonInactive,
            DealershipId = DealershipIds.Saigon,
            Name = "Linh Pham",
            IsActive = false,
        },
        new()
        {
            Id = TechnicianIds.LondonActive,
            DealershipId = DealershipIds.London,
            Name = "James Wilson",
            IsActive = true,
        },
        new()
        {
            Id = TechnicianIds.LondonInactive,
            DealershipId = DealershipIds.London,
            Name = "Emma Brown",
            IsActive = false,
        },
    ];

    public static TechnicianQualification[] Qualifications { get; } =
    [
        new()
        {
            TechnicianId = TechnicianIds.SaigonActive,
            ServiceTypeId = ServiceTypeIds.SaigonOilChange,
            DealershipId = DealershipIds.Saigon,
        },
        new()
        {
            TechnicianId = TechnicianIds.SaigonActive,
            ServiceTypeId = ServiceTypeIds.SaigonBrakeService,
            DealershipId = DealershipIds.Saigon,
        },
        new()
        {
            TechnicianId = TechnicianIds.SaigonInactive,
            ServiceTypeId = ServiceTypeIds.SaigonOilChange,
            DealershipId = DealershipIds.Saigon,
        },
        new()
        {
            TechnicianId = TechnicianIds.LondonActive,
            ServiceTypeId = ServiceTypeIds.LondonAnnualService,
            DealershipId = DealershipIds.London,
        },
        new()
        {
            TechnicianId = TechnicianIds.LondonInactive,
            ServiceTypeId = ServiceTypeIds.LondonAnnualService,
            DealershipId = DealershipIds.London,
        },
    ];

    public static ServiceBay[] ServiceBays { get; } =
    [
        new()
        {
            Id = ServiceBayIds.SaigonActive,
            DealershipId = DealershipIds.Saigon,
            Name = "Bay 1",
            IsActive = true,
        },
        new()
        {
            Id = ServiceBayIds.SaigonInactive,
            DealershipId = DealershipIds.Saigon,
            Name = "Bay 2",
            IsActive = false,
        },
        new()
        {
            Id = ServiceBayIds.LondonActive,
            DealershipId = DealershipIds.London,
            Name = "Bay 1",
            IsActive = true,
        },
        new()
        {
            Id = ServiceBayIds.LondonInactive,
            DealershipId = DealershipIds.London,
            Name = "Bay 2",
            IsActive = false,
        },
    ];

    private static IEnumerable<DealershipOpeningHour> BuildWeekdayOpeningHours(Guid dealershipId)
    {
        for (var day = 1; day <= 5; day++)
        {
            yield return new DealershipOpeningHour
            {
                DealershipId = dealershipId,
                DayOfWeek = (byte)day,
                OpensAt = new TimeOnly(9, 0),
                ClosesAt = new TimeOnly(17, 0),
            };
        }
    }
}
