using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Features.Availability;

/// <summary>Evaluation status for an availability request.</summary>
public enum AvailabilityStatus
{
    Evaluated,
    NotFound,
    InvalidInterval,
}

/// <summary>
/// Advisory availability result. A valid request with no eligible pair is evaluated with
/// <see cref="Available"/> false; only missing references and invalid intervals use other statuses.
/// </summary>
public sealed record AvailabilityEvaluation(
    AvailabilityStatus Status,
    bool Available,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt)
{
    public static AvailabilityEvaluation NotFound() =>
        new(AvailabilityStatus.NotFound, false, default, default);

    public static AvailabilityEvaluation InvalidInterval() =>
        new(AvailabilityStatus.InvalidInterval, false, default, default);

    public static AvailabilityEvaluation Evaluated(
        bool available,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(AvailabilityStatus.Evaluated, available, startsAt, endsAt);
}

/// <summary>
/// Evaluates an advisory availability request using authoritative SQL Server duration, dealership
/// local opening hours, technician qualification/shift coverage, and independent technician and bay
/// conflict checks. No data is written and Redis is never consulted.
/// </summary>
public sealed class AvailabilityService(SchedulerDbContext dbContext)
{
    public async Task<AvailabilityEvaluation> EvaluateAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        using var activity = ServiceSchedulerTelemetry.ActivitySource.StartActivity("availability.evaluate");
        try
        {
            var evaluation = await EvaluateCoreAsync(
                    dealershipId,
                    serviceTypeId,
                    startsAtUtc,
                    evaluatedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("availability.result", MapOutcome(evaluation));
            return evaluation;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            activity?.SetTag("availability.result", "error");
            activity?.SetStatus(ActivityStatusCode.Error, "error");
            throw;
        }
    }

    private static string MapOutcome(AvailabilityEvaluation evaluation) => evaluation.Status switch
    {
        AvailabilityStatus.NotFound => "not_found",
        AvailabilityStatus.InvalidInterval => "invalid_interval",
        _ => evaluation.Available ? "available" : "unavailable",
    };

    private async Task<AvailabilityEvaluation> EvaluateCoreAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var lookup = await AvailabilityQueries
            .ServiceLookups(dbContext, dealershipId, serviceTypeId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lookup is null)
        {
            return AvailabilityEvaluation.NotFound();
        }

        if (!AvailabilityInterval.TryCreate(
                startsAtUtc,
                evaluatedAtUtc,
                lookup.DurationMinutes,
                out var endsAtUtc))
        {
            return AvailabilityEvaluation.InvalidInterval();
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(lookup.TimeZoneId);
        if (!OpeningCoverage.TryGetLocalInterval(
                startsAtUtc,
                endsAtUtc,
                timeZone,
                out var localStart,
                out var localEnd))
        {
            return AvailabilityEvaluation.Evaluated(false, startsAtUtc, endsAtUtc);
        }

        var localDay = TimeZoneInfo.ConvertTime(startsAtUtc, timeZone).DayOfWeek;
        var withinOpeningHours = await AvailabilityQueries
            .OpeningStarts(dbContext, dealershipId, (byte)localDay, localStart, localEnd)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!withinOpeningHours)
        {
            return AvailabilityEvaluation.Evaluated(false, startsAtUtc, endsAtUtc);
        }

        var technicianId = await AvailabilityQueries
            .TechnicianCandidates(dbContext, dealershipId, serviceTypeId, startsAtUtc, endsAtUtc)
            .Select(id => (Guid?)id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var bayId = await AvailabilityQueries
            .BayCandidates(dbContext, dealershipId, startsAtUtc, endsAtUtc)
            .Select(id => (Guid?)id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return AvailabilityEvaluation.Evaluated(
            technicianId is not null && bayId is not null,
            startsAtUtc,
            endsAtUtc);
    }
}
