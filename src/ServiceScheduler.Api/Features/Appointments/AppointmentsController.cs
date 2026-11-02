using Microsoft.AspNetCore.Mvc;
using ServiceScheduler.Api.Features.Appointments.Contracts;
using ServiceScheduler.Api.Features.Availability;
using ServiceScheduler.Api.Infrastructure.Errors;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Features.Appointments;

[ApiController]
[Route("api/dealerships/{dealershipId}/appointments")]
public sealed class AppointmentsController(AppointmentService appointmentService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AppointmentResponse>> CreateAsync(
        Guid dealershipId,
        [FromBody] CreateAppointmentRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || request.CustomerId is null
            || request.CustomerId == Guid.Empty
            || request.VehicleId is null
            || request.VehicleId == Guid.Empty
            || request.ServiceTypeId is null
            || request.ServiceTypeId == Guid.Empty)
        {
            ServiceSchedulerTelemetry.RecordBookingOutcome("validation_error");
            return ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "customerId, vehicleId, serviceTypeId, and startsAt are required.",
                ProblemCodes.ValidationError);
        }

        if (!StartsAtParser.TryParse(request.StartsAt, out var parsedStartsAt))
        {
            ServiceSchedulerTelemetry.RecordBookingOutcome("validation_error");
            return ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "startsAt must be a valid timestamp with Z or an explicit offset.",
                ProblemCodes.ValidationError);
        }

        var result = await appointmentService
            .CreateAsync(
                dealershipId,
                request.CustomerId.Value,
                request.VehicleId.Value,
                request.ServiceTypeId.Value,
                parsedStartsAt.ToUniversalTime(),
                cancellationToken)
            .ConfigureAwait(false);

        ServiceSchedulerTelemetry.RecordBookingOutcome(MapOutcome(result));
        return result.Status switch
        {
            AppointmentCreationStatus.Created when result.Appointment is not null =>
                CreatedAtAction(
                    nameof(GetById),
                    new { dealershipId, appointmentId = result.Appointment.AppointmentId },
                    ToResponse(result.Appointment)),
            AppointmentCreationStatus.ValidationError => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "The request is invalid.",
                "The requested appointment interval or ownership is invalid.",
                ProblemCodes.ValidationError),
            AppointmentCreationStatus.NotFound => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                "The requested resource was not found.",
                "The dealership, customer, vehicle, or service type was not found for this dealership.",
                ProblemCodes.NotFound),
            AppointmentCreationStatus.CapacityUnavailable => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                "The requested interval is unavailable.",
                "No eligible technician and service bay pair could be reserved for the requested interval within the configured allocation limit.",
                ProblemCodes.CapacityUnavailable),
            AppointmentCreationStatus.DependencyUnavailable => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status503ServiceUnavailable,
                "The service is temporarily unavailable.",
                "A required dependency is unavailable. Retry the request later.",
                ProblemCodes.DependencyUnavailable),
            _ => ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "The appointment could not be confirmed.",
                ProblemCodes.InternalError),
        };
    }

    [HttpGet("{appointmentId}")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AppointmentResponse>> GetById(
        Guid dealershipId,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await appointmentService
            .GetAsync(dealershipId, appointmentId, cancellationToken)
            .ConfigureAwait(false);

        return appointment is null
            ? ProblemResponse.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                "The requested resource was not found.",
                "The appointment was not found for this dealership.",
                ProblemCodes.NotFound)
            : Ok(ToResponse(appointment));
    }

    private static string MapOutcome(AppointmentCreationResult result) =>
        result.Status == AppointmentCreationStatus.Created && result.Appointment is not null
            ? "created"
            : result.Status switch
            {
                AppointmentCreationStatus.ValidationError => "validation_error",
                AppointmentCreationStatus.NotFound => "not_found",
                AppointmentCreationStatus.CapacityUnavailable => "capacity_unavailable",
                AppointmentCreationStatus.DependencyUnavailable => "dependency_unavailable",
                _ => "internal_error",
            };

    private static AppointmentResponse ToResponse(CreatedAppointment appointment) =>
        new(
            appointment.AppointmentId,
            appointment.DealershipId,
            appointment.CustomerId,
            appointment.VehicleId,
            appointment.ServiceTypeId,
            appointment.TechnicianId,
            appointment.ServiceBayId,
            appointment.StartUtc.UtcDateTime,
            appointment.EndUtc.UtcDateTime,
            appointment.DurationMinutes,
            appointment.Status,
            appointment.CreatedUtc.UtcDateTime);
}
