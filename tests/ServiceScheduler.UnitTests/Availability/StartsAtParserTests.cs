using ServiceScheduler.Api.Features.Availability;
using Xunit;

namespace ServiceScheduler.UnitTests.Availability;

public sealed class StartsAtParserTests
{
    [Theory]
    [InlineData("2026-11-03T03:00:00Z")]
    [InlineData("2026-11-03T03:00:00z")]
    [InlineData("2026-11-03T10:00:00+07:00")]
    [InlineData("2026-11-03T10:00:00+0700")]
    [InlineData("2026-11-02T22:00:00-05:00")]
    [InlineData(" 2026-11-03T03:00:00Z ")]
    public void ExplicitOffsetValuesAreParsed(string rawValue)
    {
        Assert.True(StartsAtParser.TryParse(rawValue, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-11-03")]
    [InlineData("2026-11-03T03:00:00")]
    [InlineData("not-a-timestamp")]
    [InlineData("2026-11-03T10:00:00+7:00")]
    public void OffsetFreeOrInvalidValuesAreRejected(string? rawValue)
    {
        Assert.False(StartsAtParser.TryParse(rawValue, out _));
    }

    [Fact]
    public void EquivalentExplicitOffsetsNormalizeToTheSameInstant()
    {
        Assert.True(StartsAtParser.TryParse("2026-11-03T03:00:00Z", out var utc));
        Assert.True(StartsAtParser.TryParse("2026-11-03T10:00:00+07:00", out var offset));

        Assert.Equal(utc.ToUniversalTime(), offset.ToUniversalTime());
    }
}
