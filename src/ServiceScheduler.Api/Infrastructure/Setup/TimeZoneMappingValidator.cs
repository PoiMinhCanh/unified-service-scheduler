using System.Data;
using Microsoft.Data.SqlClient;

namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Validates the IANA-to-Windows time-zone mapping and the booking procedure before the runtime
/// principal is provisioned. SQL Server cannot use IANA identifiers in <c>AT TIME ZONE</c>; setup
/// therefore fails when a mapping is missing, SQL rejects the Windows identifier, or the .NET and
/// SQL local conversions disagree for representative instants.
/// </summary>
public sealed class TimeZoneMappingValidator
{
    private static readonly DateTimeOffset DefaultParityInstant = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, DateTimeOffset[]> ParityFixtures =
        new Dictionary<string, DateTimeOffset[]>(StringComparer.Ordinal)
        {
            ["Asia/Ho_Chi_Minh"] =
            [
                DefaultParityInstant,
            ],
            ["Europe/London"] =
            [
                new(2027, 3, 28, 1, 30, 0, TimeSpan.Zero),
                new(2027, 3, 28, 3, 0, 0, TimeSpan.Zero),
                new(2027, 10, 31, 0, 30, 0, TimeSpan.Zero),
                new(2027, 10, 31, 2, 0, 0, TimeSpan.Zero),
            ],
        };

    private readonly string _setupConnectionString;

    public TimeZoneMappingValidator(string setupConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupConnectionString);
        _setupConnectionString = setupConnectionString;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_setupConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ValidateProcedureExistsAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidateDealershipCoverageAsync(connection, cancellationToken).ConfigureAwait(false);

        var mappings = await ReadMappingsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (mappings.Count == 0)
        {
            throw new InvalidOperationException(
                "No SQL time-zone mappings were found; the ticket 3 migration must seed at least one mapping.");
        }

        foreach (var mapping in mappings)
        {
            var zone = ResolveIanaZone(mapping.IanaId);
            foreach (var instant in GetParityFixtures(mapping.IanaId))
            {
                await ValidateParityAsync(connection, mapping, zone, instant, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task ValidateProcedureExistsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT OBJECT_ID(N'dbo.CreateAppointment', N'P');";
        await using var command = new SqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not int)
        {
            throw new InvalidOperationException(
                "The booking procedure dbo.CreateAppointment was not found; apply the ticket 3 migration before setup.");
        }
    }

    private static async Task ValidateDealershipCoverageAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT dealership.TimeZoneId
            FROM dbo.Dealerships AS dealership
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.TimeZoneMappings AS mapping
                WHERE mapping.IanaId = dealership.TimeZoneId)
            ORDER BY dealership.TimeZoneId;
            """;

        var missing = new List<string>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            missing.Add(reader.GetString(0));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Dealership time zones without a SQL mapping: " + string.Join(", ", missing) + ".");
        }
    }

    private static async Task<List<TimeZoneMappingRow>> ReadMappingsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT IanaId, WindowsId
            FROM dbo.TimeZoneMappings
            ORDER BY IanaId;
            """;

        var mappings = new List<TimeZoneMappingRow>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            mappings.Add(new TimeZoneMappingRow(reader.GetString(0), reader.GetString(1)));
        }

        return mappings;
    }

    private static TimeZoneInfo ResolveIanaZone(string ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"IANA time zone '{ianaId}' could not be resolved by .NET.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException(
                $"IANA time zone '{ianaId}' is invalid for .NET.", exception);
        }
    }

    private static IEnumerable<DateTimeOffset> GetParityFixtures(string ianaId) =>
        ParityFixtures.TryGetValue(ianaId, out var fixtures) ? fixtures : [DefaultParityInstant];

    private static async Task ValidateParityAsync(
        SqlConnection connection,
        TimeZoneMappingRow mapping,
        TimeZoneInfo zone,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CONVERT(datetimeoffset(7), @instant) AT TIME ZONE @windowsId;
            """;

        DateTimeOffset sqlLocal;
        try
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@instant", SqlDbType.DateTimeOffset).Value = instant;
            command.Parameters.Add("@windowsId", SqlDbType.NVarChar, 100).Value = mapping.WindowsId;
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            sqlLocal = result is DateTimeOffset value
                ? value
                : throw new InvalidOperationException(
                    $"SQL Server did not return a time-zone conversion for '{mapping.WindowsId}'.");
        }
        catch (SqlException exception) when (exception.Number == 9820)
        {
            throw new InvalidOperationException(
                $"SQL Server rejected Windows time zone '{mapping.WindowsId}' for IANA time zone '{mapping.IanaId}'.",
                exception);
        }

        var netLocal = TimeZoneInfo.ConvertTime(instant, zone);
        if (sqlLocal.Offset != netLocal.Offset || sqlLocal.DateTime.Ticks != netLocal.DateTime.Ticks)
        {
            throw new InvalidOperationException(
                $"The .NET and SQL time-zone conversions disagree for '{mapping.IanaId}' / " +
                $"'{mapping.WindowsId}' at {instant:O}: .NET {netLocal:O}, SQL {sqlLocal:O}.");
        }
    }

    private sealed record TimeZoneMappingRow(string IanaId, string WindowsId);
}
