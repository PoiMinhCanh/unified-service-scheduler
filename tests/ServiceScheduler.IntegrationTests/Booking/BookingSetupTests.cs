using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class BookingSetupTests : SqlServerTestBase
{
    private static readonly DateTimeOffset StartsAtUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public BookingSetupTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RepeatedSetupIsIdempotentAndKeepsExactPermissions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftsBefore = await ScalarIntAsync(
            SetupConnectionString, "SELECT COUNT(*) FROM dbo.TechnicianShifts;", cancellationToken);

        await Fixture.RerunSetupAsync(Database, cancellationToken);
        await Fixture.RerunSetupAsync(Database, cancellationToken);

        var shiftsAfter = await ScalarIntAsync(
            SetupConnectionString, "SELECT COUNT(*) FROM dbo.TechnicianShifts;", cancellationToken);
        Assert.Equal(shiftsBefore, shiftsAfter);

        var rows = await QueryPermissionRowsAsync(SetupConnectionString, cancellationToken);
        Assert.Equal(6, rows.Count);
        Assert.Contains($"DATABASE|CONNECT|GRANT|{Database.DatabaseName}", rows);
        Assert.Contains("OBJECT_OR_COLUMN|EXECUTE|GRANT|dbo.CreateAppointment", rows);
        Assert.Contains("OBJECT_OR_COLUMN|INSERT|DENY|dbo.Appointments", rows);
        Assert.Contains("OBJECT_OR_COLUMN|UPDATE|DENY|dbo.Appointments", rows);
        Assert.Contains("OBJECT_OR_COLUMN|DELETE|DENY|dbo.Appointments", rows);
        Assert.Contains("SCHEMA|SELECT|GRANT|dbo", rows);
    }

    [Fact]
    public async Task FreshSetupOnAnEmptyDatabaseAppliesProcedureAndPermissions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await Fixture.CreateEmptyDatabaseAsync(nameof(BookingSetupTests), cancellationToken);

        try
        {
            await Fixture.RerunSetupAsync(database, cancellationToken);

            Assert.Equal(1, await ScalarIntAsync(
                database.SetupConnectionString,
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));
            Assert.Equal(2, await ScalarIntAsync(
                database.SetupConnectionString,
                "SELECT COUNT(*) FROM dbo.TimeZoneMappings;",
                cancellationToken));

            var rows = await QueryPermissionRowsAsync(database.SetupConnectionString, cancellationToken);
            Assert.Equal(6, rows.Count);
            Assert.Contains($"DATABASE|CONNECT|GRANT|{database.DatabaseName}", rows);
            Assert.Contains("OBJECT_OR_COLUMN|EXECUTE|GRANT|dbo.CreateAppointment", rows);
            Assert.Contains("OBJECT_OR_COLUMN|INSERT|DENY|dbo.Appointments", rows);
            Assert.Contains("OBJECT_OR_COLUMN|UPDATE|DENY|dbo.Appointments", rows);
            Assert.Contains("OBJECT_OR_COLUMN|DELETE|DENY|dbo.Appointments", rows);
            Assert.Contains("SCHEMA|SELECT|GRANT|dbo", rows);

            var invoker = new BookingProcedureInvoker(database.RuntimeConnectionString);
            var result = await invoker.ExecuteAsync(
                new BookingProcedureCallRequest(
                    SeedData.DealershipIds.Saigon,
                    SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    SeedData.TechnicianIds.SaigonActive,
                    SeedData.ServiceBayIds.SaigonActive,
                    StartsAtUtc,
                    EvaluatedAtUtc),
                cancellationToken: cancellationToken);

            Assert.NotEqual(Guid.Empty, result.AppointmentId);
            Assert.Equal(StartsAtUtc.AddMinutes(60), result.EndUtc);
        }
        finally
        {
            await Fixture.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    [Fact]
    public async Task MissingProcedureFailsSetupWithAClearMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await Fixture.CreateEmptyDatabaseAsync(nameof(BookingSetupTests), cancellationToken);

        try
        {
            await MigrateLatestAsync(database.SetupConnectionString, cancellationToken);
            await ExecuteSetupAsync(
                database.SetupConnectionString,
                "DROP PROCEDURE dbo.CreateAppointment;",
                cancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(database, cancellationToken));
            Assert.Contains("CreateAppointment", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await Fixture.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    [Fact]
    public async Task InvalidAndDstInconsistentMappingsFailSetup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await TestData.UpdateTimeZoneMappingWindowsIdAsync(
                "Europe/London", "Not/AZone", cancellationToken);
            var invalid = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("Not/AZone", invalid.Message, StringComparison.Ordinal);
        }
        finally
        {
            await TestData.UpdateTimeZoneMappingWindowsIdAsync(
                "Europe/London", "GMT Standard Time", CancellationToken.None);
        }

        try
        {
            await TestData.UpdateTimeZoneMappingWindowsIdAsync(
                "Europe/London", "Central Europe Standard Time", cancellationToken);
            var inconsistent = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("disagree", inconsistent.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await TestData.UpdateTimeZoneMappingWindowsIdAsync(
                "Europe/London", "GMT Standard Time", CancellationToken.None);
        }

        await Fixture.RerunSetupAsync(Database, cancellationToken);
    }

    private static async Task MigrateLatestAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new SchedulerDbContext(options);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task ExecuteSetupAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
