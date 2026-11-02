using System.Data;
using Microsoft.Data.SqlClient;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Executes the booking procedure through the restricted runtime connection so tests can verify
/// the database boundary without API prechecks.
/// </summary>
public sealed class BookingProcedureInvoker(string runtimeConnectionString)
{
    private readonly string _runtimeConnectionString =
        string.IsNullOrWhiteSpace(runtimeConnectionString)
            ? throw new ArgumentException("A runtime connection string is required.", nameof(runtimeConnectionString))
            : runtimeConnectionString;

    public async Task<BookingProcedureCallResult> ExecuteAsync(
        BookingProcedureCallRequest request,
        int commandTimeoutSeconds = 10,
        bool useOuterTransaction = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new SqlConnection(_runtimeConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = useOuterTransaction
            ? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        await using var command = new SqlCommand("dbo.CreateAppointment", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = commandTimeoutSeconds,
        };
        command.Transaction = transaction;

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
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }

        return new BookingProcedureCallResult(
            (Guid)appointmentId.Value,
            (DateTimeOffset)endUtc.Value,
            (int)durationMinutes.Value,
            (DateTimeOffset)createdUtc.Value);
    }
}

public sealed record BookingProcedureCallRequest(
    Guid DealershipId,
    Guid CustomerId,
    Guid VehicleId,
    Guid ServiceTypeId,
    Guid TechnicianId,
    Guid ServiceBayId,
    DateTimeOffset StartUtc,
    DateTimeOffset EvaluatedAtUtc);

public sealed record BookingProcedureCallResult(
    Guid AppointmentId,
    DateTimeOffset EndUtc,
    int DurationMinutes,
    DateTimeOffset CreatedUtc);
