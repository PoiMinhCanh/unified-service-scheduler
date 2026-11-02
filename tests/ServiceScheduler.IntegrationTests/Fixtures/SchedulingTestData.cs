using System.Data;
using Microsoft.Data.SqlClient;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Inserts and removes synthetic probe rows through the privileged setup connection. Tests must
/// delete only their own rows and must respect the existing foreign keys and check constraints.
/// </summary>
public sealed class SchedulingTestData(string setupConnectionString)
{
    private readonly string _setupConnectionString = setupConnectionString;

    public Task InsertDealershipAsync(
        Guid id,
        string name,
        string timeZoneId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.Dealerships (Id, Name, TimeZoneId)
            VALUES (@Id, @Name, @TimeZoneId);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddString(command, "@Name", name, 200);
                AddString(command, "@TimeZoneId", timeZoneId, 100);
            },
            cancellationToken);

    public Task InsertServiceTypeAsync(
        Guid id,
        Guid dealershipId,
        string name,
        int durationMinutes,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.ServiceTypes (Id, DealershipId, Name, DurationMinutes)
            VALUES (@Id, @DealershipId, @Name, @DurationMinutes);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddGuid(command, "@DealershipId", dealershipId);
                AddString(command, "@Name", name, 200);
                AddInt(command, "@DurationMinutes", durationMinutes);
            },
            cancellationToken);

    public Task InsertTechnicianAsync(
        Guid id,
        Guid dealershipId,
        string name,
        bool isActive,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.Technicians (Id, DealershipId, Name, IsActive)
            VALUES (@Id, @DealershipId, @Name, @IsActive);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddGuid(command, "@DealershipId", dealershipId);
                AddString(command, "@Name", name, 200);
                AddBoolean(command, "@IsActive", isActive);
            },
            cancellationToken);

    public Task InsertQualificationAsync(
        Guid technicianId,
        Guid serviceTypeId,
        Guid dealershipId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.TechnicianQualifications (TechnicianId, ServiceTypeId, DealershipId)
            VALUES (@TechnicianId, @ServiceTypeId, @DealershipId);
            """,
            command =>
            {
                AddGuid(command, "@TechnicianId", technicianId);
                AddGuid(command, "@ServiceTypeId", serviceTypeId);
                AddGuid(command, "@DealershipId", dealershipId);
            },
            cancellationToken);

    public Task InsertShiftAsync(
        Guid id,
        Guid technicianId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.TechnicianShifts (Id, TechnicianId, StartUtc, EndUtc)
            VALUES (@Id, @TechnicianId, @StartUtc, @EndUtc);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddGuid(command, "@TechnicianId", technicianId);
                AddDateTimeOffset(command, "@StartUtc", startUtc);
                AddDateTimeOffset(command, "@EndUtc", endUtc);
            },
            cancellationToken);

    public Task InsertBayAsync(
        Guid id,
        Guid dealershipId,
        string name,
        bool isActive,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.ServiceBays (Id, DealershipId, Name, IsActive)
            VALUES (@Id, @DealershipId, @Name, @IsActive);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddGuid(command, "@DealershipId", dealershipId);
                AddString(command, "@Name", name, 200);
                AddBoolean(command, "@IsActive", isActive);
            },
            cancellationToken);

    public Task InsertOpeningHoursAsync(
        Guid dealershipId,
        byte dayOfWeek,
        TimeOnly opensAt,
        TimeOnly closesAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.DealershipOpeningHours (DealershipId, DayOfWeek, OpensAt, ClosesAt)
            VALUES (@DealershipId, @DayOfWeek, @OpensAt, @ClosesAt);
            """,
            command =>
            {
                AddGuid(command, "@DealershipId", dealershipId);
                AddTinyInt(command, "@DayOfWeek", dayOfWeek);
                AddTime(command, "@OpensAt", opensAt);
                AddTime(command, "@ClosesAt", closesAt);
            },
            cancellationToken);

    public Task InsertAppointmentAsync(
        Guid id,
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        Guid technicianId,
        Guid serviceBayId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        var durationMinutes = (int)(endUtc - startUtc).TotalMinutes;
        if (durationMinutes <= 0 || startUtc.AddMinutes(durationMinutes) != endUtc)
        {
            throw new ArgumentException("The appointment interval must be a positive whole number of minutes.", nameof(endUtc));
        }

        return ExecuteAsync(
            """
            INSERT INTO dbo.Appointments
                (Id, DealershipId, CustomerId, VehicleId, ServiceTypeId, TechnicianId, ServiceBayId,
                 StartUtc, EndUtc, DurationMinutes, Status, CreatedUtc)
            VALUES
                (@Id, @DealershipId, @CustomerId, @VehicleId, @ServiceTypeId, @TechnicianId, @ServiceBayId,
                 @StartUtc, @EndUtc, @DurationMinutes, N'Confirmed', @CreatedUtc);
            """,
            command =>
            {
                AddGuid(command, "@Id", id);
                AddGuid(command, "@DealershipId", dealershipId);
                AddGuid(command, "@CustomerId", customerId);
                AddGuid(command, "@VehicleId", vehicleId);
                AddGuid(command, "@ServiceTypeId", serviceTypeId);
                AddGuid(command, "@TechnicianId", technicianId);
                AddGuid(command, "@ServiceBayId", serviceBayId);
                AddDateTimeOffset(command, "@StartUtc", startUtc);
                AddDateTimeOffset(command, "@EndUtc", endUtc);
                AddInt(command, "@DurationMinutes", durationMinutes);
                AddDateTimeOffset(command, "@CreatedUtc", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
            },
            cancellationToken);
    }

    public Task UpdateServiceTypeNameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE dbo.ServiceTypes SET Name = @Name WHERE Id = @Id;",
            command =>
            {
                AddGuid(command, "@Id", id);
                AddString(command, "@Name", name, 200);
            },
            cancellationToken);

    public Task UpdateServiceTypeDurationAsync(
        Guid id,
        int durationMinutes,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE dbo.ServiceTypes SET DurationMinutes = @DurationMinutes WHERE Id = @Id;",
            command =>
            {
                AddGuid(command, "@Id", id);
                AddInt(command, "@DurationMinutes", durationMinutes);
            },
            cancellationToken);

    public Task SetTechnicianActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE dbo.Technicians SET IsActive = @IsActive WHERE Id = @Id;",
            command =>
            {
                AddGuid(command, "@Id", id);
                AddBoolean(command, "@IsActive", isActive);
            },
            cancellationToken);

    public Task SetBayActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE dbo.ServiceBays SET IsActive = @IsActive WHERE Id = @Id;",
            command =>
            {
                AddGuid(command, "@Id", id);
                AddBoolean(command, "@IsActive", isActive);
            },
            cancellationToken);

    public Task UpdateTimeZoneMappingWindowsIdAsync(
        string ianaId,
        string windowsId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE dbo.TimeZoneMappings SET WindowsId = @WindowsId WHERE IanaId = @IanaId;",
            command =>
            {
                AddString(command, "@IanaId", ianaId, 100);
                AddString(command, "@WindowsId", windowsId, 100);
            },
            cancellationToken);

    public Task InsertTimeZoneMappingAsync(
        string ianaId,
        string windowsId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "INSERT INTO dbo.TimeZoneMappings (IanaId, WindowsId) VALUES (@IanaId, @WindowsId);",
            command =>
            {
                AddString(command, "@IanaId", ianaId, 100);
                AddString(command, "@WindowsId", windowsId, 100);
            },
            cancellationToken);

    public Task DeleteTimeZoneMappingAsync(string ianaId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM dbo.TimeZoneMappings WHERE IanaId = @IanaId;",
            command => AddString(command, "@IanaId", ianaId, 100),
            cancellationToken);

    public async Task<int> CountAppointmentsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_setupConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Appointments;", connection);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task DeleteAppointmentAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.Appointments WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    public Task DeleteShiftAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.TechnicianShifts WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    public Task DeleteQualificationAsync(Guid technicianId, Guid serviceTypeId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM dbo.TechnicianQualifications WHERE TechnicianId = @TechnicianId AND ServiceTypeId = @ServiceTypeId;",
            command =>
            {
                AddGuid(command, "@TechnicianId", technicianId);
                AddGuid(command, "@ServiceTypeId", serviceTypeId);
            },
            cancellationToken);

    public Task DeleteTechnicianAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.Technicians WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    public Task DeleteBayAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.ServiceBays WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    public Task DeleteOpeningHoursAsync(
        Guid dealershipId,
        byte dayOfWeek,
        TimeOnly opensAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM dbo.DealershipOpeningHours WHERE DealershipId = @DealershipId AND DayOfWeek = @DayOfWeek AND OpensAt = @OpensAt;",
            command =>
            {
                AddGuid(command, "@DealershipId", dealershipId);
                AddTinyInt(command, "@DayOfWeek", dayOfWeek);
                AddTime(command, "@OpensAt", opensAt);
            },
            cancellationToken);

    public Task DeleteServiceTypeAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.ServiceTypes WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    public Task DeleteDealershipAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM dbo.Dealerships WHERE Id = @Id;", command => AddGuid(command, "@Id", id), cancellationToken);

    private async Task ExecuteAsync(
        string sql,
        Action<SqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_setupConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        addParameters(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddGuid(SqlCommand command, string name, Guid value) =>
        command.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = value;

    private static void AddString(SqlCommand command, string name, string value, int size) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value;

    private static void AddBoolean(SqlCommand command, string name, bool value) =>
        command.Parameters.Add(name, SqlDbType.Bit).Value = value;

    private static void AddTinyInt(SqlCommand command, string name, byte value) =>
        command.Parameters.Add(name, SqlDbType.TinyInt).Value = value;

    private static void AddTime(SqlCommand command, string name, TimeOnly value) =>
        command.Parameters.Add(name, SqlDbType.Time).Value = value.ToTimeSpan();

    private static void AddDateTimeOffset(SqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.Add(name, SqlDbType.DateTimeOffset).Value = value;
}
