using ServiceScheduler.Api.Features.Availability;
using Xunit;

namespace ServiceScheduler.UnitTests.Availability;

public sealed class OpeningCoverageTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static readonly TimeZoneInfo Saigon = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    [Theory]
    [InlineData(9, 0, 17, 0, 9, 0, 17, 0, true)]
    [InlineData(9, 0, 17, 0, 9, 0, 16, 59, true)]
    [InlineData(9, 0, 17, 0, 8, 59, 17, 0, false)]
    [InlineData(9, 0, 17, 0, 9, 0, 17, 1, false)]
    public void BoundariesAreInclusive(
        int opensHour,
        int opensMinute,
        int closesHour,
        int closesMinute,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        bool expected)
    {
        var result = OpeningCoverage.IsWithin(
            new TimeOnly(startHour, startMinute),
            new TimeOnly(endHour, endMinute),
            new TimeOnly(opensHour, opensMinute),
            new TimeOnly(closesHour, closesMinute));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IntervalAcrossLocalMidnightIsNotACoveredLocalInterval()
    {
        var startsAt = new DateTimeOffset(2026, 11, 3, 16, 30, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddMinutes(60);

        Assert.False(OpeningCoverage.TryGetLocalInterval(startsAt, endsAt, Saigon, out _, out _));
    }

    [Fact]
    public void SpringForwardGapUsesWallClockComparison()
    {
        var startsAt = new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddMinutes(90);

        Assert.True(OpeningCoverage.TryGetLocalInterval(startsAt, endsAt, London, out var localStart, out var localEnd));
        Assert.Equal(new TimeOnly(2, 30), localStart);
        Assert.Equal(new TimeOnly(4, 0), localEnd);
        Assert.True(OpeningCoverage.IsWithin(localStart, localEnd, new TimeOnly(2, 0), new TimeOnly(4, 0)));
        Assert.True(OpeningCoverage.IsWithin(localStart, localEnd, new TimeOnly(1, 0), new TimeOnly(4, 0)));
    }

    [Fact]
    public void FallBackRepeatedHourUsesWallClockComparison()
    {
        var startsAt = new DateTimeOffset(2027, 10, 31, 0, 30, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddMinutes(90);

        Assert.True(OpeningCoverage.TryGetLocalInterval(startsAt, endsAt, London, out var localStart, out var localEnd));
        Assert.Equal(new TimeOnly(1, 30), localStart);
        Assert.Equal(new TimeOnly(2, 0), localEnd);
        Assert.True(OpeningCoverage.IsWithin(localStart, localEnd, new TimeOnly(0, 0), new TimeOnly(4, 0)));
    }
}
