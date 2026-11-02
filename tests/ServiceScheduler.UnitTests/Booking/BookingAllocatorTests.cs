using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.Appointments;
using ServiceScheduler.Api.Infrastructure.Persistence;
using Xunit;

namespace ServiceScheduler.UnitTests.Booking;

public sealed class BookingAllocatorTests
{
    private static readonly Guid DealershipId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid ServiceTypeId = Guid.NewGuid();
    private static readonly DateTimeOffset StartUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstPairSuccessReturnsTheChosenPairAndProcedureResult()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        var technician = Guid.NewGuid();
        var bay = Guid.NewGuid();

        var result = await AllocateAsync(allocator, clock, [technician], [bay]);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        Assert.Equal(technician, result.TechnicianId);
        Assert.Equal(bay, result.ServiceBayId);
        Assert.NotNull(result.Procedure);
        Assert.Single(procedure.Requests);
        Assert.Equal(EvaluatedAtUtc, procedure.Requests[0].EvaluatedAtUtc);
    }

    [Fact]
    public async Task TechnicianConflictSkipsTheTechnicianAndTriesAnotherPair()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.TechnicianConflict,
            BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        var firstTechnician = Guid.NewGuid();
        var secondTechnician = Guid.NewGuid();
        var bay = Guid.NewGuid();

        var result = await AllocateAsync(
            allocator,
            clock,
            [firstTechnician, secondTechnician],
            [bay, Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        Assert.Equal(secondTechnician, result.TechnicianId);
        Assert.Equal(bay, result.ServiceBayId);
        Assert.Equal(2, procedure.Requests.Count);
    }

    [Fact]
    public async Task BayConflictSkipsTheBayAndTriesAnotherPair()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.BayConflict,
            BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        var technician = Guid.NewGuid();
        var firstBay = Guid.NewGuid();
        var secondBay = Guid.NewGuid();

        var result = await AllocateAsync(allocator, clock, [technician], [firstBay, secondBay]);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        Assert.Equal(technician, result.TechnicianId);
        Assert.Equal(secondBay, result.ServiceBayId);
        Assert.Equal(2, procedure.Requests.Count);
    }

    [Theory]
    [InlineData(BookingProcedureStatus.ResourceNotEligible)]
    [InlineData(BookingProcedureStatus.CoverageUnavailable)]
    public async Task PairSpecificFailureTriesTheNextPair(BookingProcedureStatus firstStatus)
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(firstStatus, BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        var technician = Guid.NewGuid();
        var firstBay = Guid.NewGuid();
        var secondBay = Guid.NewGuid();

        var result = await AllocateAsync(allocator, clock, [technician], [firstBay, secondBay]);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        Assert.Equal(secondBay, result.ServiceBayId);
        Assert.Equal(2, procedure.Requests.Count);
    }

    [Fact]
    public async Task ExhaustedCandidatesReturnCapacityUnavailable()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.TechnicianConflict);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.CapacityUnavailable, result.Status);
        Assert.Single(procedure.Requests);
    }

    [Fact]
    public async Task InvalidRequestReturnsValidationErrorWithoutRetry()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.InvalidRequest);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.ValidationError, result.Status);
        Assert.Single(procedure.Requests);
    }

    [Fact]
    public async Task InvalidTimeZoneMappingReturnsInternalErrorWithoutRetry()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.TimeZoneMappingInvalid);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.InternalError, result.Status);
        Assert.Single(procedure.Requests);
    }

    [Fact]
    public async Task DeadlockRetriesTheSamePairThenSucceeds()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        var technician = Guid.NewGuid();
        var bay = Guid.NewGuid();

        var result = await AllocateAsync(allocator, clock, [technician], [bay]);

        Assert.Equal(BookingAllocationStatus.Succeeded, result.Status);
        Assert.Equal(3, procedure.Requests.Count);
        Assert.All(procedure.Requests, request =>
        {
            Assert.Equal(technician, request.TechnicianId);
            Assert.Equal(bay, request.ServiceBayId);
        });
    }

    [Fact]
    public async Task ExhaustedDeadlocksReturnDependencyUnavailable()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        Assert.Equal(3, procedure.Requests.Count);
    }

    [Fact]
    public async Task DeadlockRetryLimitIsGlobalAcrossCandidatePairs()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.ResourceNotEligible,
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(
            allocator,
            clock,
            [Guid.NewGuid()],
            [Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        Assert.Equal(4, procedure.Requests.Count);
    }

    [Theory]
    [InlineData(BookingProcedureStatus.LockTimeout)]
    [InlineData(BookingProcedureStatus.CommandTimeout)]
    [InlineData(BookingProcedureStatus.DependencyUnavailable)]
    public async Task DependencyAndTimeoutFailuresDoNotRetry(BookingProcedureStatus status)
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(status);
        var allocator = CreateAllocator(procedure, clock);

        var result = await AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        Assert.Single(procedure.Requests);
    }

    [Fact]
    public async Task ExpiredBudgetReturnsDependencyWithoutInvokingTheProcedure()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.Succeeded);
        var options = new BookingOptions
        {
            RequestBudgetSeconds = 2,
            CommandTimeoutSeconds = 1,
            RetryDelayMilliseconds = 0,
        };
        var allocator = CreateAllocator(procedure, clock, options);

        var result = await AllocateAsync(
            allocator,
            clock,
            [Guid.NewGuid()],
            [Guid.NewGuid()],
            clock.GetUtcNow().AddSeconds(-3));

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        Assert.Empty(procedure.Requests);
    }

    [Fact]
    public async Task BudgetExpiringDuringDeadlockBackoffStopsBeforeTheNextProcedureCall()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Succeeded);
        var options = new BookingOptions
        {
            RequestBudgetSeconds = 1,
            RetryDelayMilliseconds = 5_000,
        };
        var allocator = CreateAllocator(procedure, clock, options);

        var allocation = AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]);

        Assert.Single(procedure.Requests);
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await allocation;

        Assert.Equal(BookingAllocationStatus.DependencyUnavailable, result.Status);
        Assert.Single(procedure.Requests);
    }

    [Fact]
    public async Task RequestCancellationPropagatesWithoutInvokingTheProcedure()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure(BookingProcedureStatus.Succeeded);
        var allocator = CreateAllocator(procedure, clock);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => allocator.AllocateAsync(
                DealershipId,
                CustomerId,
                VehicleId,
                ServiceTypeId,
                StartUtc,
                [Guid.NewGuid()],
                [Guid.NewGuid()],
                clock.GetUtcNow(),
                cancellationTokenSource.Token));

        Assert.Empty(procedure.Requests);
    }

    [Fact]
    public async Task UnexpectedProcedureExceptionPropagates()
    {
        var clock = CreateClock();
        var procedure = new FakeAppointmentBookingProcedure
        {
            Handler = static (_, _) => Task.FromException<BookingProcedureResult>(
                new InvalidOperationException("unexpected")),
        };
        var allocator = CreateAllocator(procedure, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AllocateAsync(allocator, clock, [Guid.NewGuid()], [Guid.NewGuid()]));
    }

    private static FakeTimeProvider CreateClock() => new(EvaluatedAtUtc);

    private static BookingAllocator CreateAllocator(
        FakeAppointmentBookingProcedure procedure,
        FakeTimeProvider clock,
        BookingOptions? options = null)
    {
        var effectiveOptions = options ?? new BookingOptions { RetryDelayMilliseconds = 0 };
        return new BookingAllocator(
            procedure,
            new BookingCandidatePlanner(),
            Options.Create(effectiveOptions),
            clock,
            NullLogger<BookingAllocator>.Instance);
    }

    private static Task<BookingAllocationResult> AllocateAsync(
        BookingAllocator allocator,
        FakeTimeProvider clock,
        IReadOnlyList<Guid> technicianIds,
        IReadOnlyList<Guid> bayIds,
        DateTimeOffset? budgetStartedAtUtc = null) =>
        allocator.AllocateAsync(
            DealershipId,
            CustomerId,
            VehicleId,
            ServiceTypeId,
            StartUtc,
            technicianIds,
            bayIds,
            budgetStartedAtUtc ?? clock.GetUtcNow(),
            TestContext.Current.CancellationToken);
}
