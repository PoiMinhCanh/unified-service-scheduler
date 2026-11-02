using System.Data;
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
public sealed class BookingMigrationTests : SqlServerTestBase
{
    private const string InitialMigrationId = "20261031092016_InitialCreate";
    private static readonly DateTimeOffset StartsAtUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public BookingMigrationTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Ticket3MigrationUpgradeAndDowngradePreservesAppointments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appointmentId = Guid.NewGuid();
        await using var migrationContext = CreateDbContext();
        var migrator = migrationContext.Database.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(InitialMigrationId, cancellationToken);

            await TestData.InsertAppointmentAsync(
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

            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
            Assert.Equal(0, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'TimeZoneMappings';",
                cancellationToken));
            Assert.Equal(0, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));

            await migrator.MigrateAsync(cancellationToken: cancellationToken);

            Assert.Equal(2, await ScalarIntAsync("SELECT COUNT(*) FROM dbo.TimeZoneMappings;", cancellationToken));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_Dealerships_TimeZoneMappings_TimeZoneId';",
                cancellationToken));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));
            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));

            await migrator.MigrateAsync(InitialMigrationId, cancellationToken);

            Assert.Equal(0, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'TimeZoneMappings';",
                cancellationToken));
            Assert.Equal(0, await ScalarIntAsync(
                "SELECT COUNT(*) FROM sys.procedures WHERE [name] = N'CreateAppointment';",
                cancellationToken));
            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
            await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task BookingProcedureSmokeCallPersistsTheExpectedAppointment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var migrationContext = CreateDbContext();
        var migrator = migrationContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        var before = await TestData.CountAppointmentsAsync(cancellationToken);
        Guid appointmentId = Guid.Empty;

        try
        {
            var result = await ExecuteCreateAppointmentAsync(cancellationToken);
            appointmentId = result.AppointmentId;

            Assert.NotEqual(Guid.Empty, result.AppointmentId);
            Assert.Equal(StartsAtUtc.AddMinutes(60), result.EndUtc);
            Assert.Equal(60, result.DurationMinutes);
            Assert.Equal(TimeSpan.Zero, result.CreatedUtc.Offset);
            Assert.Equal(before + 1, await TestData.CountAppointmentsAsync(cancellationToken));

            await using var dbContext = CreateDbContext();
            var appointment = await dbContext.Appointments
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == appointmentId, cancellationToken);

            Assert.Equal(StartsAtUtc, appointment.StartUtc);
            Assert.Equal(StartsAtUtc.AddMinutes(60), appointment.EndUtc);
            Assert.Equal(60, appointment.DurationMinutes);
            Assert.Equal("Confirmed", appointment.Status);
            Assert.Equal(TimeSpan.Zero, appointment.StartUtc.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.EndUtc.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.CreatedUtc.Offset);
        }
        finally
        {
            if (appointmentId != Guid.Empty)
            {
                await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None);
            }
        }
    }

    private SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(SetupConnectionString)
            .Options;
        return new SchedulerDbContext(options);
    }

    private async Task<int> ScalarIntAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<ProcedureResult> ExecuteCreateAppointmentAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("dbo.CreateAppointment", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 10,
        };

        command.Parameters.Add("@DealershipId", SqlDbType.UniqueIdentifier).Value = SeedData.DealershipIds.Saigon;
        command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value = SeedData.CustomerIds.AnNguyen;
        command.Parameters.Add("@VehicleId", SqlDbType.UniqueIdentifier).Value = SeedData.VehicleIds.AnNguyenVehicle;
        command.Parameters.Add("@ServiceTypeId", SqlDbType.UniqueIdentifier).Value = SeedData.ServiceTypeIds.SaigonOilChange;
        command.Parameters.Add("@TechnicianId", SqlDbType.UniqueIdentifier).Value = SeedData.TechnicianIds.SaigonActive;
        command.Parameters.Add("@ServiceBayId", SqlDbType.UniqueIdentifier).Value = SeedData.ServiceBayIds.SaigonActive;
        command.Parameters.Add("@StartUtc", SqlDbType.DateTimeOffset).Value = StartsAtUtc;
        command.Parameters.Add("@EvaluatedAtUtc", SqlDbType.DateTimeOffset).Value = EvaluatedAtUtc;

        var appointmentId = command.Parameters.Add("@AppointmentId", SqlDbType.UniqueIdentifier);
        appointmentId.Direction = ParameterDirection.Output;
        var endUtc = command.Parameters.Add("@EndUtc", SqlDbType.DateTimeOffset);
        endUtc.Direction = ParameterDirection.Output;
        var durationMinutes = command.Parameters.Add("@DurationMinutes", SqlDbType.Int);
        durationMinutes.Direction = ParameterDirection.Output;
        var createdUtc = command.Parameters.Add("@CreatedUtc", SqlDbType.DateTimeOffset);
        createdUtc.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new ProcedureResult(
            (Guid)appointmentId.Value,
            (DateTimeOffset)endUtc.Value,
            (int)durationMinutes.Value,
            (DateTimeOffset)createdUtc.Value);
    }

    private sealed record ProcedureResult(
        Guid AppointmentId,
        DateTimeOffset EndUtc,
        int DurationMinutes,
        DateTimeOffset CreatedUtc);
}
