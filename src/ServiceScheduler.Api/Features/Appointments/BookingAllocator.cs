using System.Diagnostics;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.Api.Features.Appointments;

public enum BookingAllocationStatus
{
    Succeeded,
    ValidationError,
    CapacityUnavailable,
    DependencyUnavailable,
    InternalError,
}

public sealed record BookingAllocationResult(
    BookingAllocationStatus Status,
    Guid TechnicianId = default,
    Guid ServiceBayId = default,
    BookingProcedureResult? Procedure = null);

/// <summary>
/// Drives the bounded candidate-pair loop. Only a SQL Server deadlock victim is retried, and only
/// within the configured global limit; lock/command timeouts, connectivity failures, and unknown
/// commit outcomes return a dependency result without replaying the write.
/// </summary>
public sealed class BookingAllocator(
    IAppointmentBookingProcedure procedure,
    BookingCandidatePlanner planner,
    IOptions<BookingOptions> options,
    TimeProvider timeProvider,
    ILogger<BookingAllocator> logger)
{
    public async Task<BookingAllocationResult> AllocateAsync(
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        IReadOnlyList<Guid> technicianIds,
        IReadOnlyList<Guid> bayIds,
        DateTimeOffset budgetStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = ServiceSchedulerTelemetry.ActivitySource.StartActivity("booking.allocate");
        var progress = new AllocationProgress();
        try
        {
            var result = await AllocateCoreAsync(
                    dealershipId,
                    customerId,
                    vehicleId,
                    serviceTypeId,
                    startUtc,
                    technicianIds,
                    bayIds,
                    budgetStartedAtUtc,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            CompleteAllocation(activity, result.Status, progress, Stopwatch.GetElapsedTime(startedAt));
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            CompleteAllocation(
                activity,
                BookingAllocationStatus.InternalError,
                progress,
                Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }

    private async Task<BookingAllocationResult> AllocateCoreAsync(
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        IReadOnlyList<Guid> technicianIds,
        IReadOnlyList<Guid> bayIds,
        DateTimeOffset budgetStartedAtUtc,
        AllocationProgress progress,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var pairs = planner.BuildPairs(technicianIds, bayIds, settings.MaxCandidateAttempts);
        if (pairs.Count == 0)
        {
            return new BookingAllocationResult(BookingAllocationStatus.CapacityUnavailable);
        }

        var conflictedTechnicians = new HashSet<Guid>();
        var conflictedBays = new HashSet<Guid>();

        foreach (var pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBudgetExhausted(budgetStartedAtUtc, settings))
            {
                logger.LogWarning(
                    "EventName {EventName} BookingOutcome {BookingOutcome} AttemptedPairs {AttemptedPairs}; booking allocation budget expired before candidate pair {Attempt}.",
                    "BookingAllocation",
                    "dependency_unavailable",
                    progress.AttemptedPairs,
                    progress.AttemptedPairs + 1);
                return new BookingAllocationResult(BookingAllocationStatus.DependencyUnavailable);
            }

            if (conflictedTechnicians.Contains(pair.TechnicianId) || conflictedBays.Contains(pair.ServiceBayId))
            {
                continue;
            }

            progress.AttemptedPairs++;

            while (true)
            {
                // Recheck the request-wide budget immediately before every procedure call,
                // including calls that follow a deadlock backoff.
                if (IsBudgetExhausted(budgetStartedAtUtc, settings))
                {
                    logger.LogWarning(
                        "EventName {EventName} BookingOutcome {BookingOutcome} AttemptedPairs {AttemptedPairs}; booking allocation budget expired before procedure attempt for candidate pair {Attempt}.",
                        "BookingAllocation",
                        "dependency_unavailable",
                        progress.AttemptedPairs,
                        progress.AttemptedPairs);
                    return new BookingAllocationResult(BookingAllocationStatus.DependencyUnavailable);
                }

                var evaluatedAtUtc = timeProvider.GetUtcNow();
                var result = await procedure.ExecuteAsync(
                    new BookingProcedureRequest(
                        dealershipId,
                        customerId,
                        vehicleId,
                        serviceTypeId,
                        pair.TechnicianId,
                        pair.ServiceBayId,
                        startUtc,
                        evaluatedAtUtc),
                    cancellationToken);

                var tryNextPair = false;
                switch (result.Status)
                {
                    case BookingProcedureStatus.Succeeded:
                        logger.LogInformation(
                            "EventName {EventName} BookingOutcome {BookingOutcome} AttemptedPairs {AttemptedPairs}; booking allocation succeeded after {AttemptedPairCount} candidate pair(s).",
                            "BookingAllocation",
                            "succeeded",
                            progress.AttemptedPairs,
                            progress.AttemptedPairs);
                        return new BookingAllocationResult(
                            BookingAllocationStatus.Succeeded,
                            pair.TechnicianId,
                            pair.ServiceBayId,
                            result);

                    case BookingProcedureStatus.TechnicianConflict:
                        conflictedTechnicians.Add(pair.TechnicianId);
                        tryNextPair = true;
                        break;

                    case BookingProcedureStatus.BayConflict:
                        conflictedBays.Add(pair.ServiceBayId);
                        tryNextPair = true;
                        break;

                    case BookingProcedureStatus.InvalidRequest:
                        return new BookingAllocationResult(BookingAllocationStatus.ValidationError);

                    case BookingProcedureStatus.ResourceNotEligible:
                    case BookingProcedureStatus.CoverageUnavailable:
                        tryNextPair = true;
                        break;

                    case BookingProcedureStatus.TimeZoneMappingInvalid:
                        logger.LogError(
                            "EventName {EventName} BookingOutcome {BookingOutcome}; booking allocation stopped because the SQL time-zone mapping is unavailable or invalid.",
                            "BookingAllocation",
                            "internal_error");
                        return new BookingAllocationResult(BookingAllocationStatus.InternalError);

                    case BookingProcedureStatus.Deadlock:
                        if (progress.DeadlockRetries >= settings.MaxDeadlockRetries)
                        {
                            logger.LogWarning(
                                "EventName {EventName} BookingOutcome {BookingOutcome} DeadlockRetries {DeadlockRetries}; booking allocation exhausted {DeadlockRetryCount} deadlock retry/retries.",
                                "BookingAllocation",
                                "dependency_unavailable",
                                progress.DeadlockRetries,
                                settings.MaxDeadlockRetries);
                            return new BookingAllocationResult(BookingAllocationStatus.DependencyUnavailable);
                        }

                        progress.DeadlockRetries++;
                        if (IsBudgetExhausted(budgetStartedAtUtc, settings))
                        {
                            return new BookingAllocationResult(BookingAllocationStatus.DependencyUnavailable);
                        }

                        if (settings.RetryDelayMilliseconds > 0)
                        {
                            await Task.Delay(
                                TimeSpan.FromMilliseconds(settings.RetryDelayMilliseconds * progress.DeadlockRetries),
                                timeProvider,
                                cancellationToken);
                        }

                        continue;

                    case BookingProcedureStatus.LockTimeout:
                    case BookingProcedureStatus.CommandTimeout:
                    case BookingProcedureStatus.DependencyUnavailable:
                        return new BookingAllocationResult(BookingAllocationStatus.DependencyUnavailable);

                    default:
                        throw new InvalidOperationException(
                            $"Unexpected booking procedure status '{result.Status}'.");
                }

                if (tryNextPair)
                {
                    break;
                }
            }
        }

        logger.LogWarning(
            "EventName {EventName} BookingOutcome {BookingOutcome} AttemptedPairs {AttemptedPairs}; booking allocation exhausted {AttemptedPairCount} candidate pair(s) without a confirmed appointment.",
            "BookingAllocation",
            "capacity_unavailable",
            progress.AttemptedPairs,
            progress.AttemptedPairs);
        return new BookingAllocationResult(BookingAllocationStatus.CapacityUnavailable);
    }

    private static void CompleteAllocation(
        Activity? activity,
        BookingAllocationStatus status,
        AllocationProgress progress,
        TimeSpan elapsed)
    {
        var outcome = MapOutcome(status);
        activity?.SetTag("booking.outcome", outcome);
        activity?.SetTag("booking.attempted_pairs", progress.AttemptedPairs);
        activity?.SetTag("booking.deadlock_retries", progress.DeadlockRetries);

        if (status is BookingAllocationStatus.DependencyUnavailable or BookingAllocationStatus.InternalError)
        {
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
        }

        ServiceSchedulerTelemetry.RecordBookingAllocationDuration(outcome, elapsed.TotalSeconds);
    }

    private static string MapOutcome(BookingAllocationStatus status) => status switch
    {
        BookingAllocationStatus.Succeeded => "succeeded",
        BookingAllocationStatus.ValidationError => "validation_error",
        BookingAllocationStatus.CapacityUnavailable => "capacity_unavailable",
        BookingAllocationStatus.DependencyUnavailable => "dependency_unavailable",
        _ => "internal_error",
    };

    private sealed class AllocationProgress
    {
        public int AttemptedPairs { get; set; }

        public int DeadlockRetries { get; set; }
    }

    private bool IsBudgetExhausted(DateTimeOffset budgetStartedAtUtc, BookingOptions settings) =>
        timeProvider.GetUtcNow() - budgetStartedAtUtc
        >= TimeSpan.FromSeconds(settings.RequestBudgetSeconds);
}
