using ServiceScheduler.Api.Features.Availability;
using Xunit;

namespace ServiceScheduler.UnitTests.Availability;

public sealed class AvailabilityIntervalTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FutureStartUsesTheAuthoritativeDuration()
    {
        var startsAt = new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

        Assert.True(AvailabilityInterval.TryCreate(startsAt, EvaluatedAt, 60, out var endsAt));
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero), endsAt);
    }

    [Fact]
    public void StartEqualToTheClockIsRejected()
    {
        Assert.False(AvailabilityInterval.TryCreate(EvaluatedAt, EvaluatedAt, 60, out _));
    }

    [Fact]
    public void PastStartIsRejected()
    {
        var startsAt = EvaluatedAt.AddMinutes(-1);

        Assert.False(AvailabilityInterval.TryCreate(startsAt, EvaluatedAt, 60, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void NonPositiveDurationIsRejected(int durationMinutes)
    {
        var startsAt = EvaluatedAt.AddHours(1);

        Assert.False(AvailabilityInterval.TryCreate(startsAt, EvaluatedAt, durationMinutes, out _));
    }

    [Fact]
    public void EndOverflowIsRejected()
    {
        var startsAt = DateTimeOffset.MaxValue.AddMinutes(-5);

        Assert.False(AvailabilityInterval.TryCreate(startsAt, EvaluatedAt, 10, out _));
    }
}
