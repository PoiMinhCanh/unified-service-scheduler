namespace ServiceScheduler.Api.Features.Appointments;

public sealed class BookingOptions
{
    public const string SectionName = "Scheduler:Booking";

    public int MaxCandidateAttempts { get; set; } = 5;

    public int MaxDeadlockRetries { get; set; } = 2;

    public int RetryDelayMilliseconds { get; set; } = 50;

    public int RequestBudgetSeconds { get; set; } = 20;

    public int CommandTimeoutSeconds { get; set; } = 10;
}
