using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.Appointments;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using ServiceScheduler.UnitTests.Booking;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class BookingAllocatorTelemetryTests
{
    private static readonly Guid DealershipId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid ServiceTypeId = Guid.NewGuid();
    private static readonly DateTimeOffset StartUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SucceededAllocationSetsBoundedOutcomeAndCounts()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var allocator = CreateAllocator(new FakeAppointmentBookingProcedure(BookingProcedureStatus.Succeeded));

        var result = await AllocateAsync(allocator);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("booking.allocate", activity.OperationName);
        Assert.Equal("succeeded", activity.GetTagItem("booking.outcome"));
        Assert.Equal(1, activity.GetTagItem("booking.attempted_pairs"));
        Assert.Equal(0, activity.GetTagItem("booking.deadlock_retries"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.BookingAllocationDurationInstrumentName
                           && Equals(measurement.Tags["outcome"], "succeeded"));
    }

    [Fact]
    public async Task CapacityFailureIsNotAnErrorStatus()
    {
        using var activities = new ActivityTestListener();
        var allocator = CreateAllocator(new FakeAppointmentBookingProcedure(BookingProcedureStatus.TechnicianConflict));

        var result = await AllocateAsync(allocator);

        Assert.Equal(BookingAllocationStatus.CapacityUnavailable, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("capacity_unavailable", activity.GetTagItem("booking.outcome"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task DependencyFailureMarksTheActivityAsError()
    {
        using var activities = new ActivityTestListener();
        var allocator = CreateAllocator(new FakeAppointmentBookingProcedure(BookingProcedureStatus.DependencyUnavailable));

        var result = await AllocateAsync(allocator);

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("dependency_unavailable", activity.GetTagItem("booking.outcome"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task ValidationFailureUsesBoundedOutcome()
    {
        using var activities = new ActivityTestListener();
        var allocator = CreateAllocator(new FakeAppointmentBookingProcedure(BookingProcedureStatus.InvalidRequest));

        var result = await AllocateAsync(allocator);

        Assert.Equal(BookingAllocationStatus.ValidationError, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("validation_error", activity.GetTagItem("booking.outcome"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task UnexpectedExceptionRecordsInternalErrorAndPropagates()
    {
        using var activities = new ActivityTestListener();
        var procedure = new FakeAppointmentBookingProcedure
        {
            Handler = static (_, _) => Task.FromException<BookingProcedureResult>(
                new InvalidOperationException("unexpected")),
        };
        var allocator = CreateAllocator(procedure);

        await Assert.ThrowsAsync<InvalidOperationException>(() => AllocateAsync(allocator));

        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("internal_error", activity.GetTagItem("booking.outcome"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    private static BookingAllocator CreateAllocator(FakeAppointmentBookingProcedure procedure)
    {
        var clock = new FakeTimeProvider(EvaluatedAtUtc);
        return new BookingAllocator(
            procedure,
            new BookingCandidatePlanner(),
            Options.Create(new BookingOptions { RetryDelayMilliseconds = 0 }),
            clock,
            NullLogger<BookingAllocator>.Instance);
    }

    private static Task<BookingAllocationResult> AllocateAsync(BookingAllocator allocator) =>
        allocator.AllocateAsync(
            DealershipId,
            CustomerId,
            VehicleId,
            ServiceTypeId,
            StartUtc,
            [Guid.NewGuid()],
            [Guid.NewGuid()],
            EvaluatedAtUtc,
            TestContext.Current.CancellationToken);
}
