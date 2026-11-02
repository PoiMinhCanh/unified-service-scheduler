using ServiceScheduler.Api.Features.Availability;
using Xunit;

namespace ServiceScheduler.UnitTests.Availability;

public sealed class HalfOpenOverlapTests
{
    private static readonly DateTimeOffset RequestedStart = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestedEnd = RequestedStart.AddMinutes(60);

    [Theory]
    [InlineData(2, 0, 3, 0, false)]
    [InlineData(2, 30, 3, 30, true)]
    [InlineData(2, 0, 4, 0, true)]
    [InlineData(3, 0, 4, 0, true)]
    [InlineData(2, 0, 5, 0, true)]
    [InlineData(3, 15, 3, 45, true)]
    public void HalfOpenTruthTable(
        int existingStartHour,
        int existingStartMinute,
        int existingEndHour,
        int existingEndMinute,
        bool expected)
    {
        var existingStart = new DateTimeOffset(2026, 11, 3, existingStartHour, existingStartMinute, 0, TimeSpan.Zero);
        var existingEnd = new DateTimeOffset(2026, 11, 3, existingEndHour, existingEndMinute, 0, TimeSpan.Zero);

        Assert.Equal(
            expected,
            HalfOpenOverlap.Overlaps(existingStart, existingEnd, RequestedStart, RequestedEnd));
    }
}
