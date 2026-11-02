using System.Data;
using Microsoft.Data.SqlClient;

namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Creates and validates the restricted runtime login and database user. The runtime principal gets
/// CONNECT and SELECT on schema dbo only; setup fails if it detects unexpected role membership,
/// explicit privileges, database or object ownership, or a user/login SID mismatch instead of
/// silently accepting that state.
/// </summary>
public sealed class RuntimePrincipalProvisioner
{
    private readonly string _setupConnectionString;
    private readonly string _runtimeLoginName;

    public RuntimePrincipalProvisioner(
        string setupConnectionString,
        string runtimeLoginName = SchedulerSetupOptions.RuntimeLoginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeLoginName);

        if (!IsValidLoginName(runtimeLoginName) || runtimeLoginName.Length > 128)
        {
            throw new ArgumentException("The runtime login name is not a valid SQL identifier.", nameof(runtimeLoginName));
        }

        _setupConnectionString = setupConnectionString;
        _runtimeLoginName = runtimeLoginName;
    }

    public async Task ProvisionAsync(string runtimePassword, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePassword);

        var setupBuilder = new SqlConnectionStringBuilder(_setupConnectionString);
        if (string.IsNullOrWhiteSpace(setupBuilder.InitialCatalog))
        {
            throw new InvalidOperationException("The setup connection string must specify an application database.");
        }

        await using (var masterConnection = new SqlConnection(WithDatabase(setupBuilder, "master")))
        {
            await masterConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureLoginAsync(masterConnection, runtimePassword, cancellationToken).ConfigureAwait(false);
            await ValidateLoginAsync(masterConnection, cancellationToken).ConfigureAwait(false);
        }

        await using (var databaseConnection = new SqlConnection(WithDatabase(setupBuilder, setupBuilder.InitialCatalog)))
        {
            await databaseConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            // Ownership is checked before the user is ensured so a database owned by the runtime
            // login is rejected even when no database user exists yet.
            await ValidateOwnershipAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
            await EnsureUserAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
            await ValidateUserAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
            await ValidateDatabasePermissionsAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
            await GrantRuntimePermissionsAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
            await ValidateDatabasePermissionsAsync(databaseConnection, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureLoginAsync(
        SqlConnection connection,
        string runtimePassword,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
            BEGIN
                DECLARE @statement nvarchar(max) =
                    N'CREATE LOGIN ' + QUOTENAME(@login) +
                    N' WITH PASSWORD = ' + QUOTENAME(@password, '''') +
                    N', CHECK_POLICY = ON;';
                EXEC sys.sp_executesql @statement;
            END;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
        command.Parameters.Add("@password", SqlDbType.NVarChar, 128).Value = runtimePassword;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateLoginAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string principalSql = """
            SELECT [type], [is_disabled]
            FROM sys.server_principals
            WHERE [name] = @login;
            """;

        bool exists;
        string type;
        bool isDisabled;

        await using (var command = new SqlCommand(principalSql, connection))
        {
            command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            exists = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                throw new InvalidOperationException($"Runtime login '{_runtimeLoginName}' was not created.");
            }

            type = reader.GetString(0);
            isDisabled = reader.GetBoolean(1);
        }

        if (!string.Equals(type, "S", StringComparison.Ordinal) || isDisabled)
        {
            throw new InvalidOperationException(
                $"Runtime login '{_runtimeLoginName}' must be an enabled SQL login.");
        }

        var roles = new List<string>();
        const string rolesSql = """
            SELECT role.[name]
            FROM sys.server_role_members AS membership
            INNER JOIN sys.server_principals AS role ON membership.[role_principal_id] = role.[principal_id]
            INNER JOIN sys.server_principals AS principal ON membership.[member_principal_id] = principal.[principal_id]
            WHERE principal.[name] = @login;
            """;

        await using (var command = new SqlCommand(rolesSql, connection))
        {
            command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                roles.Add(reader.GetString(0));
            }
        }

        if (roles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Runtime login '{_runtimeLoginName}' has unexpected server role membership: {string.Join(", ", roles)}.");
        }

        const string permissionsSql = """
            SELECT permission.[permission_name]
            FROM sys.server_permissions AS permission
            INNER JOIN sys.server_principals AS principal ON permission.[grantee_principal_id] = principal.[principal_id]
            WHERE principal.[name] = @login
              AND permission.[state] IN ('G', 'W', 'D')
              AND NOT (permission.[permission_name] = N'CONNECT SQL' AND permission.[state] = 'G');
            """;

        var serverPermissions = new List<string>();
        await using (var command = new SqlCommand(permissionsSql, connection))
        {
            command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                serverPermissions.Add(reader.GetString(0));
            }
        }

        if (serverPermissions.Count > 0)
        {
            throw new InvalidOperationException(
                $"Runtime login '{_runtimeLoginName}' has unexpected server permissions: {string.Join(", ", serverPermissions)}.");
        }
    }

    private async Task EnsureUserAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = @login)
            BEGIN
                DECLARE @statement nvarchar(max) =
                    N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
                EXEC sys.sp_executesql @statement;
            END;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateUserAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string principalSql = """
            SELECT database_principal.[type], database_principal.[sid], server_principal.[sid]
            FROM sys.database_principals AS database_principal
            LEFT JOIN sys.server_principals AS server_principal
                ON server_principal.[name] = database_principal.[name]
            WHERE database_principal.[name] = @login;
            """;

        bool exists;
        string type;
        bool sidMatches;

        await using (var command = new SqlCommand(principalSql, connection))
        {
            command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            exists = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                throw new InvalidOperationException($"Runtime user '{_runtimeLoginName}' was not created.");
            }

            type = reader.GetString(0);
            var databaseSid = reader.GetFieldValue<byte[]>(1);
            var serverSid = reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2);
            sidMatches = serverSid is not null && databaseSid.AsSpan().SequenceEqual(serverSid);
        }

        if (!string.Equals(type, "S", StringComparison.Ordinal) || !sidMatches)
        {
            throw new InvalidOperationException(
                $"Runtime user '{_runtimeLoginName}' must be a SQL user mapped to the matching server login.");
        }

        const string rolesSql = """
            SELECT role.[name]
            FROM sys.database_role_members AS membership
            INNER JOIN sys.database_principals AS role ON membership.[role_principal_id] = role.[principal_id]
            INNER JOIN sys.database_principals AS principal ON membership.[member_principal_id] = principal.[principal_id]
            WHERE principal.[name] = @login;
            """;

        var roles = new List<string>();
        await using (var command = new SqlCommand(rolesSql, connection))
        {
            command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                roles.Add(reader.GetString(0));
            }
        }

        if (roles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Runtime user '{_runtimeLoginName}' has unexpected database role membership: {string.Join(", ", roles)}.");
        }
    }

    private async Task ValidateOwnershipAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string ownershipSql = """
            SELECT
                CASE WHEN (SELECT [owner_sid] FROM sys.databases WHERE [database_id] = DB_ID()) = SUSER_SID(@login)
                    THEN N'DATABASE' END,
                CASE WHEN (SELECT [principal_id] FROM sys.schemas WHERE [schema_id] = SCHEMA_ID(N'dbo')) = USER_ID(@login)
                    THEN N'SCHEMA dbo' END,
                CASE WHEN (SELECT [principal_id] FROM sys.objects WHERE [object_id] = OBJECT_ID(N'dbo.CreateAppointment', N'P')) = USER_ID(@login)
                    THEN N'PROCEDURE dbo.CreateAppointment' END,
                CASE WHEN (SELECT [principal_id] FROM sys.objects WHERE [object_id] = OBJECT_ID(N'dbo.Appointments', N'U')) = USER_ID(@login)
                    THEN N'TABLE dbo.Appointments' END;
            """;

        var ownedObjects = new List<string>();
        await using var command = new SqlCommand(ownershipSql, connection);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The runtime ownership state could not be read.");
        }

        for (var ordinal = 0; ordinal < 4; ordinal++)
        {
            if (!reader.IsDBNull(ordinal))
            {
                ownedObjects.Add(reader.GetString(ordinal));
            }
        }

        if (ownedObjects.Count > 0)
        {
            throw new InvalidOperationException(
                $"Runtime user '{_runtimeLoginName}' must not own the application database or booking objects: " +
                $"{string.Join(", ", ownedObjects)}.");
        }
    }

    private async Task ValidateDatabasePermissionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemaId = await GetDboSchemaIdAsync(connection, cancellationToken).ConfigureAwait(false);
        var procedureId = await GetObjectIdAsync(
            connection, "dbo", "CreateAppointment", "P", cancellationToken).ConfigureAwait(false);
        var appointmentsTableId = await GetObjectIdAsync(
            connection, "dbo", "Appointments", "U", cancellationToken).ConfigureAwait(false);

        const string permissionsSql = """
            SELECT [class_desc], [permission_name], [state_desc], [major_id]
            FROM sys.database_permissions
            WHERE [grantee_principal_id] = USER_ID(@login)
              AND [state] IN ('G', 'W', 'D');
            """;

        await using var command = new SqlCommand(permissionsSql, connection);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var classDescription = reader.GetString(0);
            var permissionName = reader.GetString(1);
            var stateDescription = reader.GetString(2);
            var majorId = reader.GetInt32(3);

            var isConnect = string.Equals(classDescription, "DATABASE", StringComparison.Ordinal)
                && string.Equals(permissionName, "CONNECT", StringComparison.Ordinal)
                && string.Equals(stateDescription, "GRANT", StringComparison.Ordinal);
            var isSchemaSelect = string.Equals(classDescription, "SCHEMA", StringComparison.Ordinal)
                && string.Equals(permissionName, "SELECT", StringComparison.Ordinal)
                && string.Equals(stateDescription, "GRANT", StringComparison.Ordinal)
                && majorId == schemaId;
            var isProcedureExecute = string.Equals(classDescription, "OBJECT_OR_COLUMN", StringComparison.Ordinal)
                && string.Equals(permissionName, "EXECUTE", StringComparison.Ordinal)
                && string.Equals(stateDescription, "GRANT", StringComparison.Ordinal)
                && majorId == procedureId;
            var isAppointmentDeny = string.Equals(classDescription, "OBJECT_OR_COLUMN", StringComparison.Ordinal)
                && string.Equals(stateDescription, "DENY", StringComparison.Ordinal)
                && majorId == appointmentsTableId
                && (string.Equals(permissionName, "INSERT", StringComparison.Ordinal)
                    || string.Equals(permissionName, "UPDATE", StringComparison.Ordinal)
                    || string.Equals(permissionName, "DELETE", StringComparison.Ordinal));

            if (!isConnect && !isSchemaSelect && !isProcedureExecute && !isAppointmentDeny)
            {
                throw new InvalidOperationException(
                    $"Runtime user '{_runtimeLoginName}' has an unexpected database permission: " +
                    $"{stateDescription} {permissionName} on {classDescription}.");
            }
        }
    }

    private static async Task<int> GetDboSchemaIdAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT SCHEMA_ID(N'dbo');";
        await using var command = new SqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int schemaId
            ? schemaId
            : throw new InvalidOperationException("The dbo schema was not found in the application database.");
    }

    private static async Task<int> GetObjectIdAsync(
        SqlConnection connection,
        string schema,
        string name,
        string type,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT OBJECT_ID(@objectName, @objectType);";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@objectName", SqlDbType.NVarChar, 256).Value = $"{schema}.{name}";
        command.Parameters.Add("@objectType", SqlDbType.NVarChar, 2).Value = type;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int objectId
            ? objectId
            : throw new InvalidOperationException(
                $"Required object '{schema}.{name}' was not found in the application database.");
    }

    private async Task GrantRuntimePermissionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CreateAppointment', N'P') IS NULL
            BEGIN
                THROW 50001, 'The booking procedure is missing; apply the ticket 3 migration before setup.', 1;
            END;

            IF OBJECT_ID(N'dbo.Appointments', N'U') IS NULL
            BEGIN
                THROW 50002, 'The appointments table is missing.', 1;
            END;

            DECLARE @statement nvarchar(max) =
                N'GRANT CONNECT TO ' + QUOTENAME(@login) + N';' +
                N'GRANT SELECT ON SCHEMA::dbo TO ' + QUOTENAME(@login) + N';' +
                N'GRANT EXECUTE ON OBJECT::dbo.CreateAppointment TO ' + QUOTENAME(@login) + N';' +
                N'DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments TO ' + QUOTENAME(@login) + N';';
            EXEC sys.sp_executesql @statement;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = _runtimeLoginName;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string WithDatabase(SqlConnectionStringBuilder builder, string database) =>
        new SqlConnectionStringBuilder(builder.ConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

    private static bool IsValidLoginName(string name) =>
        name.Length <= 128
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(static character => char.IsLetterOrDigit(character) || character == '_');
}
