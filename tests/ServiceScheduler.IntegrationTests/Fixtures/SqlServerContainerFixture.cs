using System.Collections.Concurrent;
using System.Data;
using DotNet.Testcontainers.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Setup;
using Testcontainers.MsSql;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Starts one real SQL Server container per test collection and initializes one uniquely named
/// database per test class through the same setup component used by --db-setup.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public const string ImageReference =
        "mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04@sha256:0ec7739e1c5ec2f57861facbe1f2b74f1d3e147c7c97edf91eeea920c5944d9c";

    public static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<string, Task<SqlTestDatabase>> _databases = new(StringComparer.Ordinal);
    private readonly string _saPassword = $"Sa_1{Guid.NewGuid():N}!";
    private readonly string _runtimePassword = $"Rt_1{Guid.NewGuid():N}!";
    private MsSqlContainer? _container;

    public async ValueTask InitializeAsync()
    {
        TestcontainersSettings.WaitStrategyTimeout = TimeSpan.FromMinutes(3);

        _container = new MsSqlBuilder(ImageReference)
            .WithPassword(_saPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is null)
        {
            return;
        }

        foreach (var database in _databases.Values)
        {
            try
            {
                var initialized = await database.ConfigureAwait(false);
                await DropDatabaseAsync(initialized.DatabaseName, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cleanup is best-effort; the container is discarded immediately afterwards.
            }
        }

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    public Task<SqlTestDatabase> GetDatabaseAsync(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        var key = testClass.FullName ?? testClass.Name;
        return _databases.GetOrAdd(key, _ => CreateDatabaseAsync(testClass.Name));
    }

    public async Task<SqlTestDatabase> CreateEmptyDatabaseAsync(
        string namePrefix,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"SchedulerProbe_{Sanitize(namePrefix)}_{Guid.NewGuid():N}";
        await CreateDatabaseInternalAsync(databaseName).ConfigureAwait(false);
        return new SqlTestDatabase(
            databaseName,
            BuildSetupConnection(databaseName),
            BuildRuntimeConnection(databaseName));
    }

    public Task DropDatabaseAsync(SqlTestDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        return DropDatabaseAsync(database.DatabaseName, cancellationToken);
    }

    public string BuildSetupConnection(string databaseName) =>
        BuildConnectionString(databaseName, "sa", _saPassword);

    public string BuildRuntimeConnection(string databaseName) =>
        BuildConnectionString(databaseName, SchedulerSetupOptions.RuntimeLoginName, _runtimePassword);

    public async Task RerunSetupAsync(SqlTestDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var options = new SchedulerSetupOptions
        {
            SetupConnectionString = database.SetupConnectionString,
            RuntimePassword = _runtimePassword,
        };
        var runner = new DatabaseSetupRunner(NullLogger<DatabaseSetupRunner>.Instance);
        var clock = new FakeTimeProvider(FixedClockUtc);
        await runner.RunAsync(options, clock, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqlTestDatabase> CreateDatabaseAsync(string testClassName)
    {
        var databaseName = $"SchedulerTest_{Sanitize(testClassName)}_{Guid.NewGuid():N}";
        await CreateDatabaseInternalAsync(databaseName).ConfigureAwait(false);

        var setupConnectionString = BuildSetupConnection(databaseName);
        var options = new SchedulerSetupOptions
        {
            SetupConnectionString = setupConnectionString,
            RuntimePassword = _runtimePassword,
        };
        var runner = new DatabaseSetupRunner(NullLogger<DatabaseSetupRunner>.Instance);
        var clock = new FakeTimeProvider(FixedClockUtc);
        await runner.RunAsync(options, clock, CancellationToken.None).ConfigureAwait(false);

        return new SqlTestDatabase(
            databaseName,
            setupConnectionString,
            BuildRuntimeConnection(databaseName));
    }

    private async Task CreateDatabaseInternalAsync(string databaseName)
    {
        ValidateDatabaseName(databaseName);

        await using var connection = new SqlConnection(BuildSetupConnection("master"));
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        if (_container is null || !IsValidDatabaseName(databaseName))
        {
            return;
        }

        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(BuildSetupConnection("master"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(
            $"IF DB_ID(@database) IS NOT NULL " +
            $"BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{databaseName}]; END;",
            connection);
        command.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = databaseName;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildConnectionString(string databaseName, string user, string password)
    {
        ValidateDatabaseName(databaseName);

        if (_container is null)
        {
            throw new InvalidOperationException("The SQL Server container has not started.");
        }

        var server = $"{_container.Hostname},{_container.GetMappedPublicPort(MsSqlBuilder.MsSqlPort)}";
        return $"Server={server};Database={databaseName};User Id={user};Password={password};" +
               "Encrypt=True;TrustServerCertificate=True;Connect Timeout=10";
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!IsValidDatabaseName(databaseName))
        {
            throw new ArgumentException("The database name contains unsupported characters.", nameof(databaseName));
        }
    }

    private static bool IsValidDatabaseName(string databaseName) =>
        databaseName.Length is > 0 and <= 128
        && databaseName.All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static string Sanitize(string value)
    {
        var sanitized = new string(value
            .Where(static character => char.IsLetterOrDigit(character))
            .Take(80)
            .ToArray());
        return sanitized.Length == 0 ? "Class" : sanitized;
    }
}

public sealed record SqlTestDatabase(
    string DatabaseName,
    string SetupConnectionString,
    string RuntimeConnectionString);
