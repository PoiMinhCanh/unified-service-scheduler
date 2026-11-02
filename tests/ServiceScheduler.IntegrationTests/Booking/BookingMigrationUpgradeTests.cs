using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class BookingMigrationUpgradeTests : SqlServerTestBase
{
    private const string InitialMigrationId = "20261031092016_InitialCreate";
    private static readonly DateTimeOffset StartsAtUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    public BookingMigrationUpgradeTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Ticket2UpgradePreservesAppointmentAndAppliesRuntimeBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await Fixture.CreateEmptyDatabaseAsync(nameof(BookingMigrationUpgradeTests), cancellationToken);
        var appointmentId = Guid.NewGuid();

        try
        {
            await using var migrationContext = CreateDbContext(database.SetupConnectionString);
            var migrator = migrationContext.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialMigrationId, cancellationToken);

            var testData = new SchedulingTestData(database.SetupConnectionString);
            await testData.InsertAppointmentAsync(
                appointmentId,
                SeedData.DealershipIds.Saigon,
                SeedData.CustomerIds.AnNguyen,
                SeedData.VehicleIds.AnNguyenVehicle,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceBayIds.SaigonActive,
                StartsAtUtc,
                StartsAtUtc.AddMinutes(60),
                cancellationToken);

            await migrator.MigrateAsync(cancellationToken: cancellationToken);
            await Fixture.RerunSetupAsync(database, cancellationToken);

            Assert.Equal(1, await ScalarIntAsync(
                database.SetupConnectionString, "SELECT COUNT(*) FROM dbo.Appointments;", cancellationToken));
            Assert.Equal(1, await ScalarIntAsync(
                database.SetupConnectionString,
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));
            Assert.Equal(2, await ScalarIntAsync(
                database.SetupConnectionString, "SELECT COUNT(*) FROM dbo.TimeZoneMappings;", cancellationToken));

            var rows = await QueryPermissionRowsAsync(database.SetupConnectionString, cancellationToken);
            Assert.Equal(6, rows.Count);
            Assert.Contains("OBJECT_OR_COLUMN|EXECUTE|GRANT|dbo.CreateAppointment", rows);
            Assert.Contains("OBJECT_OR_COLUMN|INSERT|DENY|dbo.Appointments", rows);

            var invoker = new BookingProcedureInvoker(database.RuntimeConnectionString);
            var result = await invoker.ExecuteAsync(
                new BookingProcedureCallRequest(
                    SeedData.DealershipIds.Saigon,
                    SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    SeedData.TechnicianIds.SaigonActive,
                    SeedData.ServiceBayIds.SaigonActive,
                    new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)),
                cancellationToken: cancellationToken);
            Assert.NotEqual(Guid.Empty, result.AppointmentId);
            Assert.Equal(2, await ScalarIntAsync(
                database.SetupConnectionString, "SELECT COUNT(*) FROM dbo.Appointments;", cancellationToken));
        }
        finally
        {
            await Fixture.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    [Fact]
    public async Task DownMigrationDropsBookingObjectsAndLeavesRecoverablePermissions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await Fixture.CreateEmptyDatabaseAsync(nameof(BookingMigrationUpgradeTests), cancellationToken);
        var appointmentId = Guid.NewGuid();

        try
        {
            await using var migrationContext = CreateDbContext(database.SetupConnectionString);
            var migrator = migrationContext.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(cancellationToken: cancellationToken);
            await Fixture.RerunSetupAsync(database, cancellationToken);

            var testData = new SchedulingTestData(database.SetupConnectionString);
            await testData.InsertAppointmentAsync(
                appointmentId,
                SeedData.DealershipIds.Saigon,
                SeedData.CustomerIds.AnNguyen,
                SeedData.VehicleIds.AnNguyenVehicle,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceBayIds.SaigonActive,
                StartsAtUtc,
                StartsAtUtc.AddMinutes(60),
                cancellationToken);

            await migrator.MigrateAsync(InitialMigrationId, cancellationToken);

            Assert.Equal(1, await ScalarIntAsync(
                database.SetupConnectionString, "SELECT COUNT(*) FROM dbo.Appointments;", cancellationToken));
            Assert.Equal(0, await ScalarIntAsync(
                database.SetupConnectionString,
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'TimeZoneMappings';",
                cancellationToken));
            Assert.Equal(0, await ScalarIntAsync(
                database.SetupConnectionString,
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));

            var rowsAfterDown = await QueryPermissionRowsAsync(database.SetupConnectionString, cancellationToken);
            Assert.Equal(5, rowsAfterDown.Count);
            Assert.Contains($"DATABASE|CONNECT|GRANT|{database.DatabaseName}", rowsAfterDown);
            Assert.Contains("SCHEMA|SELECT|GRANT|dbo", rowsAfterDown);
            Assert.Contains("OBJECT_OR_COLUMN|INSERT|DENY|dbo.Appointments", rowsAfterDown);
            Assert.Contains("OBJECT_OR_COLUMN|UPDATE|DENY|dbo.Appointments", rowsAfterDown);
            Assert.Contains("OBJECT_OR_COLUMN|DELETE|DENY|dbo.Appointments", rowsAfterDown);
            Assert.DoesNotContain(rowsAfterDown, row => row.Contains("EXECUTE", StringComparison.Ordinal));

            await ExecuteAsync(
                database.SetupConnectionString,
                "REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments FROM [scheduler_app];",
                cancellationToken);

            var rowsAfterRevoke = await QueryPermissionRowsAsync(database.SetupConnectionString, cancellationToken);
            Assert.Equal(2, rowsAfterRevoke.Count);
            Assert.Contains($"DATABASE|CONNECT|GRANT|{database.DatabaseName}", rowsAfterRevoke);
            Assert.Contains("SCHEMA|SELECT|GRANT|dbo", rowsAfterRevoke);
        }
        finally
        {
            await Fixture.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    private static SchedulerDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SchedulerDbContext(options);
    }

    private static async Task<int> ScalarIntAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<string>> QueryPermissionRowsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT permission.class_desc,
                   permission.permission_name,
                   permission.state_desc,
                   CASE permission.class_desc
                       WHEN N'OBJECT_OR_COLUMN'
                           THEN OBJECT_SCHEMA_NAME(permission.major_id) + N'.' + OBJECT_NAME(permission.major_id)
                       WHEN N'SCHEMA'
                           THEN SCHEMA_NAME(permission.major_id)
                       ELSE DB_NAME()
                   END
            FROM sys.database_permissions AS permission
            WHERE permission.grantee_principal_id = USER_ID(N'scheduler_app')
            ORDER BY permission.class_desc, permission.permission_name, permission.state_desc;
            """;

        var rows = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                $"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}");
        }

        return rows;
    }
}
