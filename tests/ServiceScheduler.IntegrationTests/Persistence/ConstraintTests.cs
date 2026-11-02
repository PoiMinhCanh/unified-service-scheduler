using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class ConstraintTests : SqlServerTestBase
{
    private static readonly DateTimeOffset ValidCreatedUtc =
        new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    public ConstraintTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task DuplicateVinIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.Vehicles (Id, CustomerId, Vin, Make, Model)
            VALUES (@Id, @CustomerId, @Vin, @Make, @Model);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@Id", probeId);
                AddUniqueIdentifier(command, "@CustomerId", SeedData.CustomerIds.AnNguyen);
                AddNVarChar(command, "@Vin", "1HGCM82633A000001", 17);
                AddNVarChar(command, "@Make", "Synthetic", 100);
                AddNVarChar(command, "@Model", "Duplicate", 100);
            },
            "UX_Vehicles_Vin",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Vehicles WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task NonPositiveServiceDurationIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.ServiceTypes (Id, DealershipId, Name, DurationMinutes)
            VALUES (@Id, @DealershipId, @Name, @DurationMinutes);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@Id", probeId);
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.Saigon);
                AddNVarChar(command, "@Name", "Invalid Duration", 200);
                AddInt(command, "@DurationMinutes", 0);
            },
            "CK_ServiceTypes_DurationMinutes",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.ServiceTypes WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task InvalidTechnicianShiftIntervalIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.TechnicianShifts (Id, TechnicianId, StartUtc, EndUtc)
            VALUES (@Id, @TechnicianId, @StartUtc, @EndUtc);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@Id", probeId);
                AddUniqueIdentifier(command, "@TechnicianId", SeedData.TechnicianIds.SaigonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 5, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 5, 8, 0, 0, TimeSpan.Zero));
            },
            "CK_TechnicianShifts_Interval",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.TechnicianShifts WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task AppointmentDurationSnapshotMismatchIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            AppointmentInsertSql,
            command =>
            {
                AddValidAppointment(command, probeId, SeedData.DealershipIds.Saigon, SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle, SeedData.ServiceTypeIds.SaigonOilChange,
                    SeedData.TechnicianIds.SaigonActive, SeedData.ServiceBayIds.SaigonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 30, 0, TimeSpan.Zero));
                AddInt(command, "@DurationMinutes", 60);
            },
            "CK_Appointments_DurationSnapshot",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task UnsupportedAppointmentStatusIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            AppointmentInsertSql,
            command =>
            {
                AddValidAppointment(command, probeId, SeedData.DealershipIds.Saigon, SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle, SeedData.ServiceTypeIds.SaigonOilChange,
                    SeedData.TechnicianIds.SaigonActive, SeedData.ServiceBayIds.SaigonActive,
                    status: "Cancelled");
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero));
                AddInt(command, "@DurationMinutes", 60);
            },
            "CK_Appointments_Status",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task CrossDealershipAppointmentIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            AppointmentInsertSql,
            command =>
            {
                AddValidAppointment(command, probeId, SeedData.DealershipIds.Saigon, SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle, SeedData.ServiceTypeIds.LondonAnnualService,
                    SeedData.TechnicianIds.SaigonActive, SeedData.ServiceBayIds.SaigonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 30, 0, TimeSpan.Zero));
                AddInt(command, "@DurationMinutes", 90);
            },
            "FK_Appointments_ServiceTypes_ServiceTypeId_DealershipId",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task CustomerVehicleMismatchIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await AssertInsertRejectedAsync(
            AppointmentInsertSql,
            command =>
            {
                AddValidAppointment(command, probeId, SeedData.DealershipIds.London, SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.OliverSmithVehicle, SeedData.ServiceTypeIds.LondonAnnualService,
                    SeedData.TechnicianIds.LondonActive, SeedData.ServiceBayIds.LondonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 30, 0, TimeSpan.Zero));
                AddInt(command, "@DurationMinutes", 90);
            },
            "FK_Appointments_Vehicles_VehicleId_CustomerId",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task CrossDealershipQualificationIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.TechnicianQualifications (TechnicianId, ServiceTypeId, DealershipId)
            VALUES (@TechnicianId, @ServiceTypeId, @DealershipId);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@TechnicianId", SeedData.TechnicianIds.SaigonActive);
                AddUniqueIdentifier(command, "@ServiceTypeId", SeedData.ServiceTypeIds.LondonAnnualService);
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.London);
            },
            "FK_TechnicianQualifications_Technicians_TechnicianId_DealershipId",
            cancellationToken);

        await AssertProbeAbsentAsync(
            """
            SELECT COUNT(*)
            FROM dbo.TechnicianQualifications
            WHERE TechnicianId = @TechnicianId AND ServiceTypeId = @ServiceTypeId;
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@TechnicianId", SeedData.TechnicianIds.SaigonActive);
                AddUniqueIdentifier(command, "@ServiceTypeId", SeedData.ServiceTypeIds.LondonAnnualService);
            },
            cancellationToken);
    }

    [Fact]
    public async Task InvalidOpeningHoursIntervalIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.DealershipOpeningHours (DealershipId, DayOfWeek, OpensAt, ClosesAt)
            VALUES (@DealershipId, @DayOfWeek, @OpensAt, @ClosesAt);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.Saigon);
                AddTinyInt(command, "@DayOfWeek", 6);
                AddTime(command, "@OpensAt", new TimeOnly(17, 0));
                AddTime(command, "@ClosesAt", new TimeOnly(9, 0));
            },
            "CK_DealershipOpeningHours_Interval",
            cancellationToken);

        await AssertProbeAbsentAsync(
            """
            SELECT COUNT(*)
            FROM dbo.DealershipOpeningHours
            WHERE DealershipId = @DealershipId AND DayOfWeek = @DayOfWeek AND OpensAt = @OpensAt;
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.Saigon);
                AddTinyInt(command, "@DayOfWeek", 6);
                AddTime(command, "@OpensAt", new TimeOnly(17, 0));
            },
            cancellationToken);
    }

    [Fact]
    public async Task InvalidOpeningHoursWeekdayIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.DealershipOpeningHours (DealershipId, DayOfWeek, OpensAt, ClosesAt)
            VALUES (@DealershipId, @DayOfWeek, @OpensAt, @ClosesAt);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.Saigon);
                AddTinyInt(command, "@DayOfWeek", 7);
                AddTime(command, "@OpensAt", new TimeOnly(9, 0));
                AddTime(command, "@ClosesAt", new TimeOnly(17, 0));
            },
            "CK_DealershipOpeningHours_DayOfWeek",
            cancellationToken);
    }

    [Fact]
    public async Task NonUtcTechnicianShiftIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();
        var twoHourOffset = TimeSpan.FromHours(2);

        await AssertInsertRejectedAsync(
            """
            INSERT INTO dbo.TechnicianShifts (Id, TechnicianId, StartUtc, EndUtc)
            VALUES (@Id, @TechnicianId, @StartUtc, @EndUtc);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@Id", probeId);
                AddUniqueIdentifier(command, "@TechnicianId", SeedData.TechnicianIds.SaigonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 6, 9, 0, 0, twoHourOffset));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 6, 17, 0, 0, twoHourOffset));
            },
            "CK_TechnicianShifts_UtcOffsets",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.TechnicianShifts WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task ValidAppointmentIsAcceptedAndRolledBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();

        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new SqlCommand(AppointmentInsertSql, connection, transaction))
        {
            AddValidAppointment(command, probeId, SeedData.DealershipIds.Saigon, SeedData.CustomerIds.AnNguyen,
                SeedData.VehicleIds.AnNguyenVehicle, SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.TechnicianIds.SaigonActive, SeedData.ServiceBayIds.SaigonActive);
            AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
            AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero));
            AddInt(command, "@DurationMinutes", 60);

            Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
        }

        await transaction.RollbackAsync(cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    [Fact]
    public async Task NonUtcAppointmentIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeId = Guid.NewGuid();
        var twoHourOffset = TimeSpan.FromHours(2);

        await AssertInsertRejectedAsync(
            AppointmentInsertSql,
            command =>
            {
                AddValidAppointment(command, probeId, SeedData.DealershipIds.Saigon, SeedData.CustomerIds.AnNguyen,
                    SeedData.VehicleIds.AnNguyenVehicle, SeedData.ServiceTypeIds.SaigonOilChange,
                    SeedData.TechnicianIds.SaigonActive, SeedData.ServiceBayIds.SaigonActive,
                    createdUtc: new DateTimeOffset(2026, 12, 1, 0, 0, 0, twoHourOffset));
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 7, 9, 0, 0, twoHourOffset));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 7, 10, 0, 0, twoHourOffset));
                AddInt(command, "@DurationMinutes", 60);
            },
            "CK_Appointments_UtcOffsets",
            cancellationToken);

        await AssertProbeAbsentAsync(
            "SELECT COUNT(*) FROM dbo.Appointments WHERE Id = @Id;",
            command => AddUniqueIdentifier(command, "@Id", probeId),
            cancellationToken);
    }

    private const string AppointmentInsertSql = """
        INSERT INTO dbo.Appointments
            (Id, DealershipId, CustomerId, VehicleId, ServiceTypeId, TechnicianId, ServiceBayId,
             StartUtc, EndUtc, DurationMinutes, Status, CreatedUtc)
        VALUES
            (@Id, @DealershipId, @CustomerId, @VehicleId, @ServiceTypeId, @TechnicianId, @ServiceBayId,
             @StartUtc, @EndUtc, @DurationMinutes, @Status, @CreatedUtc);
        """;

    private static void AddValidAppointment(
        SqlCommand command,
        Guid id,
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        Guid technicianId,
        Guid serviceBayId,
        string status = AppointmentStatuses.Confirmed,
        DateTimeOffset? createdUtc = null)
    {
        AddUniqueIdentifier(command, "@Id", id);
        AddUniqueIdentifier(command, "@DealershipId", dealershipId);
        AddUniqueIdentifier(command, "@CustomerId", customerId);
        AddUniqueIdentifier(command, "@VehicleId", vehicleId);
        AddUniqueIdentifier(command, "@ServiceTypeId", serviceTypeId);
        AddUniqueIdentifier(command, "@TechnicianId", technicianId);
        AddUniqueIdentifier(command, "@ServiceBayId", serviceBayId);
        AddNVarChar(command, "@Status", status, 20);
        AddDateTimeOffset(command, "@CreatedUtc", createdUtc ?? ValidCreatedUtc);
    }

    private async Task AssertInsertRejectedAsync(
        string sql,
        Action<SqlCommand> addParameters,
        string expectedConstraint,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        addParameters(command);

        var exception = await Assert.ThrowsAsync<SqlException>(
            async () => await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false));
        Assert.Contains(expectedConstraint, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertProbeAbsentAsync(
        string sql,
        Action<SqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        addParameters(command);
        var count = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        Assert.Equal(0, count);
    }

    private static void AddUniqueIdentifier(SqlCommand command, string name, Guid value) =>
        command.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = value;

    private static void AddNVarChar(SqlCommand command, string name, string value, int size) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;

    private static void AddDateTimeOffset(SqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.Add(name, SqlDbType.DateTimeOffset).Value = value;

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value;

    private static void AddTinyInt(SqlCommand command, string name, byte value) =>
        command.Parameters.Add(name, SqlDbType.TinyInt).Value = value;

    private static void AddTime(SqlCommand command, string name, TimeOnly value) =>
        command.Parameters.Add(name, SqlDbType.Time).Value = value.ToTimeSpan();
}
