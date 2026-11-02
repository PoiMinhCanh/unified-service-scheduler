using ServiceScheduler.Api.Features.Appointments;
using Xunit;

namespace ServiceScheduler.UnitTests.Booking;

public sealed class BookingCandidatePlannerTests
{
    private readonly BookingCandidatePlanner _planner = new();

    [Fact]
    public void PairsPreserveTechnicianThenBayOrderAndDeduplicateCandidates()
    {
        var technician1 = Guid.NewGuid();
        var technician2 = Guid.NewGuid();
        var bay1 = Guid.NewGuid();
        var bay2 = Guid.NewGuid();

        var pairs = _planner.BuildPairs(
            [technician1, technician2, technician1],
            [bay1, bay2, bay1],
            10);

        Assert.Equal(
        [
            new BookingCandidatePair(technician1, bay1),
            new BookingCandidatePair(technician1, bay2),
            new BookingCandidatePair(technician2, bay1),
            new BookingCandidatePair(technician2, bay2),
        ],
        pairs);
    }

    [Fact]
    public void AttemptCapLimitsPairsInOrder()
    {
        var technician1 = Guid.NewGuid();
        var technician2 = Guid.NewGuid();
        var bay1 = Guid.NewGuid();
        var bay2 = Guid.NewGuid();

        var pairs = _planner.BuildPairs([technician1, technician2], [bay1, bay2], 2);

        Assert.Equal(
        [
            new BookingCandidatePair(technician1, bay1),
            new BookingCandidatePair(technician1, bay2),
        ],
        pairs);
    }

    [Fact]
    public void EmptyGuidsAreIgnored()
    {
        var technician = Guid.NewGuid();
        var bay = Guid.NewGuid();

        var pairs = _planner.BuildPairs([Guid.Empty, technician], [bay, Guid.Empty], 5);

        Assert.Equal([new BookingCandidatePair(technician, bay)], pairs);
    }

    [Fact]
    public void EmptyCandidateListsProduceNoPairs()
    {
        Assert.Empty(_planner.BuildPairs([], [Guid.NewGuid()], 5));
        Assert.Empty(_planner.BuildPairs([Guid.NewGuid()], [], 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAttemptLimitIsRejected(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _planner.BuildPairs([Guid.NewGuid()], [Guid.NewGuid()], maxAttempts));
    }
}
