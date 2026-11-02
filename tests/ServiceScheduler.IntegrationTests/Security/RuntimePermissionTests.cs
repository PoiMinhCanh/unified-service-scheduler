using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Security;

[Collection(SqlServerCollection.Name)]
public sealed class RuntimePermissionTests : SqlServerTestBase
{
    public RuntimePermissionTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RuntimeReadsSeededDataAsRestrictedUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var runtime = new SqlConnection(RuntimeConnectionString);
        await runtime.OpenAsync(cancellationToken);

        Assert.Equal("scheduler_app", await ExecuteScalarStringAsync(runtime, "SELECT CURRENT_USER;", cancellationToken));
        Assert.Equal(2, await ExecuteScalarIntAsync(runtime, "SELECT COUNT(*) FROM dbo.Dealerships;", cancellationToken));
    }

    [Fact]
    public async Task RuntimeCannotWriteAppointmentsDirectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var runtime = new SqlConnection(RuntimeConnectionString);
        await runtime.OpenAsync(cancellationToken);

        var probeId = Guid.NewGuid();
        await AssertPermissionDeniedAsync(
            runtime,
            """
            INSERT INTO dbo.Appointments
                (Id, DealershipId, CustomerId, VehicleId, ServiceTypeId, TechnicianId, ServiceBayId,
                 StartUtc, EndUtc, DurationMinutes, Status, CreatedUtc)
            VALUES
                (@Id, @DealershipId, @CustomerId, @VehicleId, @ServiceTypeId, @TechnicianId, @ServiceBayId,
                 @StartUtc, @EndUtc, @DurationMinutes, @Status, @CreatedUtc);
            """,
            command =>
            {
                AddUniqueIdentifier(command, "@Id", probeId);
                AddUniqueIdentifier(command, "@DealershipId", SeedData.DealershipIds.Saigon);
                AddUniqueIdentifier(command, "@CustomerId", SeedData.CustomerIds.AnNguyen);
                AddUniqueIdentifier(command, "@VehicleId", SeedData.VehicleIds.AnNguyenVehicle);
                AddUniqueIdentifier(command, "@ServiceTypeId", SeedData.ServiceTypeIds.SaigonOilChange);
                AddUniqueIdentifier(command, "@TechnicianId", SeedData.TechnicianIds.SaigonActive);
                AddUniqueIdentifier(command, "@ServiceBayId", SeedData.ServiceBayIds.SaigonActive);
                AddDateTimeOffset(command, "@StartUtc", new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));
                AddDateTimeOffset(command, "@EndUtc", new DateTimeOffset(2027, 1, 4, 10, 0, 0, TimeSpan.Zero));
                AddInt(command, "@DurationMinutes", 60);
                AddNVarChar(command, "@Status", "Confirmed", 20);
                AddDateTimeOffset(command, "@CreatedUtc", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
            },
            cancellationToken);

        await AssertPermissionDeniedAsync(
            runtime,
            "UPDATE dbo.Appointments SET Status = N'Confirmed';",
            null,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            runtime,
            "DELETE FROM dbo.Appointments;",
            null,
            cancellationToken);

        Assert.Equal(0, await ExecuteSetupScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.Appointments;",
            cancellationToken));
    }

    [Fact]
    public async Task RuntimeCannotChangeSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var runtime = new SqlConnection(RuntimeConnectionString);
        await runtime.OpenAsync(cancellationToken);

        await AssertPermissionDeniedAsync(
            runtime,
            "CREATE TABLE dbo.RuntimeProbe (Id int NOT NULL);",
            null,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            runtime,
            "ALTER TABLE dbo.Dealerships ADD RuntimeProbeColumn int NULL;",
            null,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            runtime,
            "DROP TABLE dbo.Dealerships;",
            null,
            cancellationToken);

        Assert.Equal(0, await ExecuteSetupScalarIntAsync(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.RuntimeProbe', N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken));
        Assert.Equal(0, await ExecuteSetupScalarIntAsync(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.Dealerships', N'RuntimeProbeColumn') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken));
        Assert.Equal(1, await ExecuteSetupScalarIntAsync(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.Dealerships', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
            cancellationToken));
    }

    [Fact]
    public async Task RuntimeHasOnlyTheExpectedPermissions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var runtime = new SqlConnection(RuntimeConnectionString);
        await runtime.OpenAsync(cancellationToken);

        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_ROLEMEMBER('db_owner');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_ROLEMEMBER('db_datawriter');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_ROLEMEMBER('db_ddladmin');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_ROLEMEMBER('db_securityadmin');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_ROLEMEMBER('db_datareader');", cancellationToken));

        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_SRVROLEMEMBER('sysadmin');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_SRVROLEMEMBER('securityadmin');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_SRVROLEMEMBER('serveradmin');", cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(runtime, "SELECT IS_SRVROLEMEMBER('dbcreator');", cancellationToken));

        Assert.Equal(1, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'SELECT');",
            cancellationToken));
        Assert.Equal(1, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo.CreateAppointment', N'OBJECT', N'EXECUTE');",
            cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo.Appointments', N'OBJECT', N'INSERT');",
            cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo.Appointments', N'OBJECT', N'UPDATE');",
            cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo.Appointments', N'OBJECT', N'DELETE');",
            cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'ALTER');",
            cancellationToken));
        Assert.Equal(0, await ExecuteScalarIntAsync(
            runtime,
            "SELECT HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'EXECUTE');",
            cancellationToken));
    }

    [Fact]
    public async Task RuntimePermissionRowsAreExact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = new SqlConnection(SetupConnectionString);
        await setup.OpenAsync(cancellationToken);

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
        await using var command = new SqlCommand(sql, setup);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                $"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}");
        }

        Assert.Equal(
        [
            $"DATABASE|CONNECT|GRANT|{Database.DatabaseName}",
            "OBJECT_OR_COLUMN|DELETE|DENY|dbo.Appointments",
            "OBJECT_OR_COLUMN|EXECUTE|GRANT|dbo.CreateAppointment",
            "OBJECT_OR_COLUMN|INSERT|DENY|dbo.Appointments",
            "OBJECT_OR_COLUMN|UPDATE|DENY|dbo.Appointments",
            "SCHEMA|SELECT|GRANT|dbo",
        ],
        rows);
    }

    [Fact]
    public async Task RuntimeCanExecuteTheBookingProcedure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invoker = new BookingProcedureInvoker(RuntimeConnectionString);
        var result = await invoker.ExecuteAsync(
            new BookingProcedureCallRequest(
                SeedData.DealershipIds.Saigon,
                SeedData.CustomerIds.AnNguyen,
                SeedData.VehicleIds.AnNguyenVehicle,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceBayIds.SaigonActive,
                new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)),
            cancellationToken: cancellationToken);

        try
        {
            Assert.NotEqual(Guid.Empty, result.AppointmentId);
            Assert.Equal(60, result.DurationMinutes);
            Assert.Equal(
                new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero),
                result.EndUtc);
            Assert.Equal(
                1,
                await ExecuteSetupScalarIntAsync(
                    $"SELECT COUNT(*) FROM dbo.Appointments WHERE Id = '{result.AppointmentId:D}';",
                    cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(result.AppointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task SetupFailsWhenRuntimeUserHasUnexpectedPermission()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var setup = new SqlConnection(SetupConnectionString))
        {
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "GRANT SELECT ON OBJECT::dbo.Appointments TO [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("unexpected", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var setup = new SqlConnection(SetupConnectionString);
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "REVOKE SELECT ON OBJECT::dbo.Appointments FROM [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await Fixture.RerunSetupAsync(Database, cancellationToken);
    }

    [Fact]
    public async Task SetupFailsWhenRuntimeUserHasUnexpectedRoleMembership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var setup = new SqlConnection(SetupConnectionString))
        {
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "ALTER ROLE db_owner ADD MEMBER [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("db_owner", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await using var setup = new SqlConnection(SetupConnectionString);
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "ALTER ROLE db_owner DROP MEMBER [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await Fixture.RerunSetupAsync(Database, cancellationToken);
    }

    [Theory]
    [InlineData("dbo.Appointments")]
    [InlineData("dbo.CreateAppointment")]
    public async Task SetupFailsWhenRuntimeUserOwnsAnApplicationObject(string objectName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var setup = new SqlConnection(SetupConnectionString))
        {
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"ALTER AUTHORIZATION ON OBJECT::{objectName} TO [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("must not own", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var setup = new SqlConnection(SetupConnectionString);
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"ALTER AUTHORIZATION ON OBJECT::{objectName} TO SCHEMA OWNER;",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await Fixture.RerunSetupAsync(Database, cancellationToken);
    }

    [Fact]
    public async Task SetupFailsWhenRuntimeUserOwnsTheApplicationDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var setup = new SqlConnection(SetupConnectionString))
        {
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"DROP USER [scheduler_app]; ALTER AUTHORIZATION ON DATABASE::[{Database.DatabaseName}] TO [scheduler_app];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Fixture.RerunSetupAsync(Database, cancellationToken));
            Assert.Contains("must not own", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var setup = new SqlConnection(SetupConnectionString);
            await setup.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"ALTER AUTHORIZATION ON DATABASE::[{Database.DatabaseName}] TO [sa];",
                setup);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await Fixture.RerunSetupAsync(Database, cancellationToken);
    }

    private async Task AssertPermissionDeniedAsync(
        SqlConnection connection,
        string sql,
        Action<SqlCommand>? addParameters,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        addParameters?.Invoke(command);

        var exception = await Assert.ThrowsAsync<SqlException>(
            async () => await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false));
        Assert.Contains("permission", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture)!;
    }

    private static async Task<int> ExecuteScalarIntAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private async Task<int> ExecuteSetupScalarIntAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(SetupConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await ExecuteScalarIntAsync(connection, sql, cancellationToken);
    }

    private static void AddUniqueIdentifier(SqlCommand command, string name, Guid value) =>
        command.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = value;

    private static void AddNVarChar(SqlCommand command, string name, string value, int size) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;

    private static void AddDateTimeOffset(SqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.Add(name, SqlDbType.DateTimeOffset).Value = value;

    private static void AddInt(SqlCommand command, string name, int value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value;
}
