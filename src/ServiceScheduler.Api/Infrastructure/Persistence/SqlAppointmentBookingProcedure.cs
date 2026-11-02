using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Features.Appointments;
using ServiceScheduler.Api.Infrastructure.Configuration;
using ServiceScheduler.Api.Infrastructure.Errors;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Infrastructure.Persistence;

/// <summary>
/// Executes the same-owner booking procedure on a dedicated runtime connection. The transaction,
/// locking, validation, and commit belong to SQL Server; this boundary only binds parameters and
/// maps known procedure errors to typed outcomes. It never retries a write.
/// </summary>
public sealed class SqlAppointmentBookingProcedure(
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<BookingOptions> bookingOptions,
    ILogger<SqlAppointmentBookingProcedure> logger) : IAppointmentBookingProcedure
{
    public async Task<BookingProcedureResult> ExecuteAsync(
        BookingProcedureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connectionString = databaseOptions.Value.Runtime;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("unexpected");
            throw new InvalidOperationException("ConnectionStrings:Runtime is required for booking writes.");
        }

        await using var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ServiceSchedulerTelemetry.RecordBookingProcedureAttempt(
                SqlFailureClassifier.IsUnavailable(exception)
                    ? "dependency_unavailable"
                    : "unexpected");
            throw;
        }

        await using var command = new SqlCommand("dbo.CreateAppointment", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = bookingOptions.Value.CommandTimeoutSeconds,
        };

        command.Parameters.Add("@DealershipId", SqlDbType.UniqueIdentifier).Value = request.DealershipId;
        command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
        command.Parameters.Add("@VehicleId", SqlDbType.UniqueIdentifier).Value = request.VehicleId;
        command.Parameters.Add("@ServiceTypeId", SqlDbType.UniqueIdentifier).Value = request.ServiceTypeId;
        command.Parameters.Add("@TechnicianId", SqlDbType.UniqueIdentifier).Value = request.TechnicianId;
        command.Parameters.Add("@ServiceBayId", SqlDbType.UniqueIdentifier).Value = request.ServiceBayId;
        command.Parameters.Add("@StartUtc", SqlDbType.DateTimeOffset).Value = request.StartUtc;
        command.Parameters.Add("@EvaluatedAtUtc", SqlDbType.DateTimeOffset).Value = request.EvaluatedAtUtc;

        var appointmentId = command.Parameters.Add("@AppointmentId", SqlDbType.UniqueIdentifier);
        appointmentId.Direction = ParameterDirection.Output;
        var endUtc = command.Parameters.Add("@EndUtc", SqlDbType.DateTimeOffset);
        endUtc.Direction = ParameterDirection.Output;
        var durationMinutes = command.Parameters.Add("@DurationMinutes", SqlDbType.Int);
        durationMinutes.Direction = ParameterDirection.Output;
        var createdUtc = command.Parameters.Add("@CreatedUtc", SqlDbType.DateTimeOffset);
        createdUtc.Direction = ParameterDirection.Output;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException exception) when (cancellationToken.IsCancellationRequested)
        {
            // SqlClient reports an attention/cancellation as a SqlException in some lock-wait paths.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (SqlException exception)
        {
            var status = MapStatus(exception.Number);
            if (status is null)
            {
                ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("unexpected");
                throw;
            }

            ServiceSchedulerTelemetry.RecordBookingProcedureAttempt(MapResult(status.Value));
            logger.LogWarning(
                "EventName {EventName} SqlErrorNumber {SqlErrorNumber} ProcedureResult {ProcedureResult}; booking procedure attempt failed and was mapped to a bounded result.",
                "BookingProcedureAttempt",
                exception.Number,
                MapResult(status.Value));
            return new BookingProcedureResult(status.Value);
        }
        catch (Exception)
        {
            ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("unexpected");
            throw;
        }

        if (appointmentId.Value is not Guid persistedAppointmentId
            || endUtc.Value is not DateTimeOffset persistedEndUtc
            || durationMinutes.Value is not int persistedDurationMinutes
            || createdUtc.Value is not DateTimeOffset persistedCreatedUtc)
        {
            ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("unexpected");
            throw new InvalidOperationException("The booking procedure returned incomplete output values.");
        }

        ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("succeeded");
        return new BookingProcedureResult(
            BookingProcedureStatus.Succeeded,
            persistedAppointmentId,
            persistedEndUtc,
            persistedDurationMinutes,
            persistedCreatedUtc);
    }

    private static BookingProcedureStatus? MapStatus(int sqlErrorNumber) => sqlErrorNumber switch
    {
        51001 => BookingProcedureStatus.TechnicianConflict,
        51002 => BookingProcedureStatus.BayConflict,
        51003 => BookingProcedureStatus.InvalidRequest,
        51004 => BookingProcedureStatus.ResourceNotEligible,
        51005 => BookingProcedureStatus.CoverageUnavailable,
        51006 => BookingProcedureStatus.TimeZoneMappingInvalid,
        1205 => BookingProcedureStatus.Deadlock,
        1222 => BookingProcedureStatus.LockTimeout,
        -2 => BookingProcedureStatus.CommandTimeout,
        _ when SqlFailureClassifier.IsUnavailable(sqlErrorNumber) => BookingProcedureStatus.DependencyUnavailable,
        _ => null,
    };

    private static string MapResult(BookingProcedureStatus status) => status switch
    {
        BookingProcedureStatus.Succeeded => "succeeded",
        BookingProcedureStatus.TechnicianConflict => "technician_conflict",
        BookingProcedureStatus.BayConflict => "bay_conflict",
        BookingProcedureStatus.InvalidRequest => "invalid_request",
        BookingProcedureStatus.ResourceNotEligible => "resource_not_eligible",
        BookingProcedureStatus.CoverageUnavailable => "coverage_unavailable",
        BookingProcedureStatus.TimeZoneMappingInvalid => "timezone_mapping_invalid",
        BookingProcedureStatus.Deadlock => "deadlock",
        BookingProcedureStatus.LockTimeout => "lock_timeout",
        BookingProcedureStatus.CommandTimeout => "command_timeout",
        BookingProcedureStatus.DependencyUnavailable => "dependency_unavailable",
        _ => "unexpected",
    };
}
