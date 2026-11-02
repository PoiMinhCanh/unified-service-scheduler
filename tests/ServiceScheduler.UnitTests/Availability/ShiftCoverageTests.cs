using ServiceScheduler.Api.Features.Availability;
using Xunit;

namespace ServiceScheduler.UnitTests.Availability;

public sealed class ShiftCoverageTests
{
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 3, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ShiftEnd = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExactBoundariesAreCovered()
    {
        Assert.True(ShiftCoverage.IsCovered(ShiftStart, ShiftEnd, ShiftStart, ShiftEnd));
    }

    [Fact]
    public void StartBeforeOpeningIsNotCovered()
    {
        Assert.False(ShiftCoverage.IsCovered(ShiftStart, ShiftEnd, ShiftStart.AddMinutes(-1), ShiftEnd));
    }

    [Fact]
    public void EndAfterClosingIsNotCovered()
    {
        Assert.False(ShiftCoverage.IsCovered(ShiftStart, ShiftEnd, ShiftStart, ShiftEnd.AddMinutes(1)));
    }

    [Fact]
    public void AdjacentOrSplitShiftsAreNotCombined()
    {
        var requestStart = ShiftStart.AddMinutes(90);
        var requestEnd = ShiftEnd.AddMinutes(30);

        Assert.False(ShiftCoverage.IsCovered(ShiftStart, ShiftStart.AddHours(2), requestStart, requestEnd));
        Assert.False(ShiftCoverage.IsCovered(ShiftStart.AddHours(2), ShiftEnd.AddHours(1), requestStart, requestEnd));
    }
}
