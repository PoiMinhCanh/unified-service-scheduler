using ServiceScheduler.Api.Features.Appointments;
using Xunit;

namespace ServiceScheduler.UnitTests.Booking;

public sealed class BookingOptionsValidatorTests
{
    private readonly BookingOptionsValidator _validator = new();

    [Fact]
    public void DefaultOptionsSucceed()
    {
        var result = _validator.Validate(null, new BookingOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void MaxCandidateAttemptsOutsideRangeFails(int value)
    {
        var options = new BookingOptions { MaxCandidateAttempts = value };

        AssertFailed(options, "MaxCandidateAttempts");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void MaxDeadlockRetriesOutsideRangeFails(int value)
    {
        var options = new BookingOptions { MaxDeadlockRetries = value };

        AssertFailed(options, "MaxDeadlockRetries");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void RetryDelayOutsideRangeFails(int value)
    {
        var options = new BookingOptions { RetryDelayMilliseconds = value };

        AssertFailed(options, "RetryDelayMilliseconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void RequestBudgetOutsideRangeFails(int value)
    {
        var options = new BookingOptions { RequestBudgetSeconds = value };

        AssertFailed(options, "RequestBudgetSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void CommandTimeoutOutsideRangeFails(int value)
    {
        var options = new BookingOptions { CommandTimeoutSeconds = value };

        AssertFailed(options, "CommandTimeoutSeconds");
    }

    [Fact]
    public void CommandTimeoutMustBeLessThanTheRequestBudget()
    {
        var options = new BookingOptions
        {
            RequestBudgetSeconds = 10,
            CommandTimeoutSeconds = 10,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("CommandTimeoutSeconds", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("RequestBudgetSeconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryValuesSucceed()
    {
        var options = new BookingOptions
        {
            MaxCandidateAttempts = 20,
            MaxDeadlockRetries = 5,
            RetryDelayMilliseconds = 1000,
            RequestBudgetSeconds = 120,
            CommandTimeoutSeconds = 60,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private void AssertFailed(BookingOptions options, string expectedFragment)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expectedFragment, result.FailureMessage, StringComparison.Ordinal);
    }
}
