using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class MigrationAndSeedTests : SqlServerTestBase
{
    public MigrationAndSeedTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task InitialMigrationCreatesTheSchemaContract()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tables = await QueryStringsAsync(
            "SELECT [name] FROM sys.tables WHERE [name] <> N'__EFMigrationsHistory' ORDER BY [name];",
            cancellationToken);
        Assert.Equal(
            [
                "Appointments",
                "Customers",
                "DealershipOpeningHours",
                "Dealerships",
                "ServiceBays",
                "ServiceTypes",
                "TechnicianQualifications",
                "TechnicianShifts",
                "Technicians",
                "TimeZoneMappings",
                "Vehicles",
            ],
            tables);

        var migrationIds = await QueryStringsAsync(
            "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];",
            cancellationToken);
        Assert.Equal(2, migrationIds.Count);
        Assert.EndsWith("_InitialCreate", migrationIds[0], StringComparison.Ordinal);
        Assert.EndsWith("_AddBookingProcedureAndTimeZoneMappings", migrationIds[1], StringComparison.Ordinal);

        var checks = await QueryStringsAsync(
            "SELECT [name] FROM sys.check_constraints ORDER BY [name];",
            cancellationToken);
        Assert.Equal(
            [
                "CK_Appointments_DurationMinutes",
                "CK_Appointments_DurationSnapshot",
                "CK_Appointments_Interval",
                "CK_Appointments_Status",
                "CK_Appointments_UtcOffsets",
                "CK_DealershipOpeningHours_DayOfWeek",
                "CK_DealershipOpeningHours_Interval",
                "CK_ServiceTypes_DurationMinutes",
                "CK_TechnicianShifts_Interval",
                "CK_TechnicianShifts_UtcOffsets",
            ],
            checks);

        var indexes = await QueryStringsAsync(
            """
            SELECT [name]
            FROM sys.indexes
            WHERE [name] IS NOT NULL
              AND [object_id] IN (SELECT [object_id] FROM sys.tables WHERE [name] <> N'__EFMigrationsHistory')
            ORDER BY [name];
            """,
            cancellationToken);
        Assert.Equal(
            [
                "AK_ServiceBays_Id_DealershipId",
                "AK_ServiceTypes_Id_DealershipId",
                "AK_Technicians_Id_DealershipId",
                "AK_Vehicles_Id_CustomerId",
                "IX_Appointments_CustomerId",
                "IX_Appointments_DealershipId_ServiceBayId_StartUtc",
                "IX_Appointments_DealershipId_TechnicianId_StartUtc",
                "IX_Appointments_ServiceTypeId_DealershipId",
                "IX_Appointments_VehicleId_CustomerId",
                "IX_TechnicianQualifications_ServiceTypeId_DealershipId",
                "IX_TechnicianShifts_TechnicianId_StartUtc",
                "IX_Technicians_DealershipId",
                "IX_Vehicles_CustomerId",
                "PK_Appointments",
                "PK_Customers",
                "PK_DealershipOpeningHours",
                "PK_Dealerships",
                "PK_ServiceBays",
                "PK_ServiceTypes",
                "PK_TechnicianQualifications",
                "PK_TechnicianShifts",
                "PK_Technicians",
                "PK_TimeZoneMappings",
                "PK_Vehicles",
                "UX_Dealerships_Name",
                "UX_ServiceBays_DealershipId_Name",
                "UX_ServiceTypes_DealershipId_Name",
                "UX_TechnicianShifts_TechnicianId_StartUtc_EndUtc",
                "UX_TimeZoneMappings_WindowsId",
                "UX_Vehicles_Vin",
            ],
            indexes);

        var foreignKeys = await QueryForeignKeyContractsAsync(cancellationToken);
        Assert.Equal(
            [
                "FK_Appointments_Customers_CustomerId|NO_ACTION|NO_ACTION",
                "FK_Appointments_Dealerships_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Appointments_ServiceBays_ServiceBayId_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Appointments_ServiceTypes_ServiceTypeId_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Appointments_Technicians_TechnicianId_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Appointments_Vehicles_VehicleId_CustomerId|NO_ACTION|NO_ACTION",
                "FK_DealershipOpeningHours_Dealerships_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Dealerships_TimeZoneMappings_TimeZoneId|NO_ACTION|NO_ACTION",
                "FK_ServiceBays_Dealerships_DealershipId|NO_ACTION|NO_ACTION",
                "FK_ServiceTypes_Dealerships_DealershipId|NO_ACTION|NO_ACTION",
                "FK_TechnicianQualifications_ServiceTypes_ServiceTypeId_DealershipId|NO_ACTION|NO_ACTION",
                "FK_TechnicianQualifications_Technicians_TechnicianId_DealershipId|NO_ACTION|NO_ACTION",
                "FK_TechnicianShifts_Technicians_TechnicianId|NO_ACTION|NO_ACTION",
                "FK_Technicians_Dealerships_DealershipId|NO_ACTION|NO_ACTION",
                "FK_Vehicles_Customers_CustomerId|NO_ACTION|NO_ACTION",
            ],
            foreignKeys);

        var procedures = await QueryStringsAsync(
            "SELECT [name] FROM sys.procedures ORDER BY [name];",
            cancellationToken);
        Assert.Equal(["CreateAppointment"], procedures);

        var procedureDefinition = Assert.Single(await QueryStringsAsync(
            "SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.CreateAppointment'));",
            cancellationToken));
        Assert.Contains("SERIALIZABLE", procedureDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDLOCK, HOLDLOCK", procedureDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XACT_ABORT ON", procedureDefinition, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            new ColumnContract("datetimeoffset", 7, null),
            await GetColumnContractAsync("Appointments", "StartUtc", cancellationToken));
        Assert.Equal(
            new ColumnContract("datetimeoffset", 7, null),
            await GetColumnContractAsync("Appointments", "CreatedUtc", cancellationToken));
        Assert.Equal(
            new ColumnContract("int", null, null),
            await GetColumnContractAsync("Appointments", "DurationMinutes", cancellationToken));
        Assert.Equal(
            new ColumnContract("nvarchar", null, 20),
            await GetColumnContractAsync("Appointments", "Status", cancellationToken));
        Assert.Equal(
            new ColumnContract("time", 0, null),
            await GetColumnContractAsync("DealershipOpeningHours", "OpensAt", cancellationToken));
        Assert.Equal(
            new ColumnContract("tinyint", null, null),
            await GetColumnContractAsync("DealershipOpeningHours", "DayOfWeek", cancellationToken));
        Assert.Equal(
            new ColumnContract("nvarchar", null, 17),
            await GetColumnContractAsync("Vehicles", "Vin", cancellationToken));
        Assert.Equal(
            new ColumnContract("nvarchar", null, 100),
            await GetColumnContractAsync("Dealerships", "TimeZoneId", cancellationToken));
        Assert.Equal(
            new ColumnContract("nvarchar", null, 100),
            await GetColumnContractAsync("TimeZoneMappings", "IanaId", cancellationToken));
        Assert.Equal(
            new ColumnContract("nvarchar", null, 100),
            await GetColumnContractAsync("TimeZoneMappings", "WindowsId", cancellationToken));
        Assert.Equal(
            new ColumnContract("datetimeoffset", 7, null),
            await GetColumnContractAsync("TechnicianShifts", "StartUtc", cancellationToken));
    }

    [Fact]
    public async Task StaticSeedsAreDeterministicAndAppointmentsAreEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = CreateDbContext();

        var dealerships = await dbContext.Dealerships.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(2, dealerships.Count);
        Assert.Contains(dealerships, dealership =>
            dealership.Id == SeedData.DealershipIds.Saigon && dealership.TimeZoneId == "Asia/Ho_Chi_Minh");
        Assert.Contains(dealerships, dealership =>
            dealership.Id == SeedData.DealershipIds.London && dealership.TimeZoneId == "Europe/London");

        var timeZoneMappings = await dbContext.TimeZoneMappings.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(2, timeZoneMappings.Count);
        Assert.Contains(timeZoneMappings, mapping =>
            mapping.IanaId == "Asia/Ho_Chi_Minh" && mapping.WindowsId == "SE Asia Standard Time");
        Assert.Contains(timeZoneMappings, mapping =>
            mapping.IanaId == "Europe/London" && mapping.WindowsId == "GMT Standard Time");

        var openingHours = await dbContext.DealershipOpeningHours.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(10, openingHours.Count);
        Assert.All(openingHours, hours =>
        {
            Assert.InRange(hours.DayOfWeek, (byte)1, (byte)5);
            Assert.Equal(new TimeOnly(9, 0), hours.OpensAt);
            Assert.Equal(new TimeOnly(17, 0), hours.ClosesAt);
        });

        Assert.Equal(2, await dbContext.Customers.AsNoTracking().CountAsync(cancellationToken));

        var vehicles = await dbContext.Vehicles.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(2, vehicles.Count);
        Assert.Contains(vehicles, vehicle =>
            vehicle.Id == SeedData.VehicleIds.AnNguyenVehicle
            && vehicle.CustomerId == SeedData.CustomerIds.AnNguyen);
        Assert.Contains(vehicles, vehicle =>
            vehicle.Id == SeedData.VehicleIds.OliverSmithVehicle
            && vehicle.CustomerId == SeedData.CustomerIds.OliverSmith);
        Assert.All(vehicles, vehicle =>
        {
            Assert.Equal(17, vehicle.Vin.Length);
            Assert.Equal(vehicle.Vin.ToUpperInvariant(), vehicle.Vin);
        });

        var serviceTypes = await dbContext.ServiceTypes.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(3, serviceTypes.Count);
        Assert.All(serviceTypes, serviceType => Assert.True(serviceType.DurationMinutes > 0));

        var technicians = await dbContext.Technicians.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(4, technicians.Count);
        Assert.Equal(2, technicians.Count(technician => technician.IsActive));
        Assert.Contains(technicians, technician =>
            technician.Id == SeedData.TechnicianIds.SaigonActive && technician.IsActive);
        Assert.Contains(technicians, technician =>
            technician.Id == SeedData.TechnicianIds.SaigonInactive && !technician.IsActive);
        Assert.Contains(technicians, technician =>
            technician.Id == SeedData.TechnicianIds.LondonActive && technician.IsActive);
        Assert.Contains(technicians, technician =>
            technician.Id == SeedData.TechnicianIds.LondonInactive && !technician.IsActive);

        var qualifications = await dbContext.TechnicianQualifications.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(5, qualifications.Count);
        var technicianDealerships = technicians.ToDictionary(technician => technician.Id, technician => technician.DealershipId);
        var serviceTypeDealerships = serviceTypes.ToDictionary(serviceType => serviceType.Id, serviceType => serviceType.DealershipId);
        Assert.All(qualifications, qualification =>
        {
            Assert.Equal(technicianDealerships[qualification.TechnicianId], qualification.DealershipId);
            Assert.Equal(serviceTypeDealerships[qualification.ServiceTypeId], qualification.DealershipId);
        });

        var bays = await dbContext.ServiceBays.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(4, bays.Count);
        Assert.Equal(2, bays.Count(bay => bay.IsActive));

        Assert.Equal(0, await dbContext.Appointments.AsNoTracking().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task FutureShiftsCoverTheHorizonAndRerunIsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = CreateDbContext();

        var activeTechnicians = await (
                from technician in dbContext.Technicians.AsNoTracking()
                join dealership in dbContext.Dealerships.AsNoTracking()
                    on technician.DealershipId equals dealership.Id
                where technician.IsActive
                select new { technician.Id, dealership.TimeZoneId })
            .ToListAsync(cancellationToken);

        Assert.Equal(2, activeTechnicians.Count);

        var shifts = await dbContext.TechnicianShifts.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(40, shifts.Count);

        foreach (var technician in activeTechnicians)
        {
            var technicianShifts = shifts
                .Where(shift => shift.TechnicianId == technician.Id)
                .OrderBy(shift => shift.StartUtc)
                .ToList();
            Assert.Equal(20, technicianShifts.Count);

            var zone = TimeZoneInfo.FindSystemTimeZoneById(technician.TimeZoneId);
            Assert.All(technicianShifts, shift =>
            {
                Assert.Equal(TimeSpan.Zero, shift.StartUtc.Offset);
                Assert.Equal(TimeSpan.Zero, shift.EndUtc.Offset);

                var localStart = TimeZoneInfo.ConvertTime(shift.StartUtc, zone);
                var localEnd = TimeZoneInfo.ConvertTime(shift.EndUtc, zone);
                Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(localStart.DateTime));
                Assert.Equal(new TimeOnly(17, 0), TimeOnly.FromDateTime(localEnd.DateTime));
                Assert.True(localStart.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
            });

            var firstLocalDay = TimeZoneInfo.ConvertTime(technicianShifts[0].StartUtc, zone).Date;
            var lastLocalDay = TimeZoneInfo.ConvertTime(technicianShifts[^1].StartUtc, zone).Date;
            Assert.Equal(new DateTime(2026, 11, 3), firstLocalDay);
            Assert.Equal(new DateTime(2026, 11, 30), lastLocalDay);
        }

        var shiftIdsBefore = shifts.Select(shift => shift.Id).OrderBy(id => id).ToList();
        var migrationsBefore = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var principalCountsBefore = await QueryRuntimePrincipalCountsAsync(cancellationToken);

        await Fixture.RerunSetupAsync(Database, cancellationToken);

        await using var reloadedDbContext = CreateDbContext();
        var shiftsAfter = await reloadedDbContext.TechnicianShifts.AsNoTracking().ToListAsync(cancellationToken);
        var shiftIdsAfter = shiftsAfter.Select(shift => shift.Id).OrderBy(id => id).ToList();
        Assert.Equal(shiftIdsBefore, shiftIdsAfter);

        var migrationsAfter = (await reloadedDbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        Assert.Equal(migrationsBefore, migrationsAfter);

        var principalCountsAfter = await QueryRuntimePrincipalCountsAsync(cancellationToken);
        Assert.Equal(principalCountsBefore, principalCountsAfter);
        Assert.Equal(1, principalCountsAfter.Logins);
        Assert.Equal(1, principalCountsAfter.Users);
        Assert.Equal(6, principalCountsAfter.Permissions);
    }

    private async Task<(int Logins, int Users, int Permissions)> QueryRuntimePrincipalCountsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM sys.server_principals WHERE [name] = N'scheduler_app'),
                (SELECT COUNT(*) FROM sys.database_principals WHERE [name] = N'scheduler_app'),
                (SELECT COUNT(*) FROM sys.database_permissions
                 WHERE [grantee_principal_id] = USER_ID(N'scheduler_app')
                   AND [state] IN ('G', 'W', 'D'));
            """;

        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(SetupConnectionString)
            .Options;
        return new SchedulerDbContext(options);
    }

    private async Task<List<string>> QueryStringsAsync(string sql, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        // SQL Server's default collation is case-insensitive. Sort in the test so assertions do
        // not depend on the database collation.
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private async Task<List<string>> QueryForeignKeyContractsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT [name], [delete_referential_action_desc], [update_referential_action_desc]
            FROM sys.foreign_keys;
            """;

        var results = new List<string>();
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private async Task<ColumnContract> GetColumnContractAsync(
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DATA_TYPE,
                   CAST(DATETIME_PRECISION AS int),
                   CAST(CHARACTER_MAXIMUM_LENGTH AS int)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = N'dbo'
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column;
            """;

        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        command.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = column;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));
        return new ColumnContract(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2));
    }

    private sealed record ColumnContract(string DataType, int? Precision, int? MaxLength);
}
