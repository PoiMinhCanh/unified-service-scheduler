using Microsoft.Extensions.Options;

namespace ServiceScheduler.Api.Features.Appointments;

public sealed class BookingOptionsValidator : IValidateOptions<BookingOptions>
{
    public ValidateOptionsResult Validate(string? name, BookingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateRange(
            failures,
            $"{BookingOptions.SectionName}:MaxCandidateAttempts",
            options.MaxCandidateAttempts,
            1,
            20);
        ValidateRange(
            failures,
            $"{BookingOptions.SectionName}:MaxDeadlockRetries",
            options.MaxDeadlockRetries,
            0,
            5);
        ValidateRange(
            failures,
            $"{BookingOptions.SectionName}:RetryDelayMilliseconds",
            options.RetryDelayMilliseconds,
            0,
            1000);
        ValidateRange(
            failures,
            $"{BookingOptions.SectionName}:RequestBudgetSeconds",
            options.RequestBudgetSeconds,
            1,
            120);
        ValidateRange(
            failures,
            $"{BookingOptions.SectionName}:CommandTimeoutSeconds",
            options.CommandTimeoutSeconds,
            1,
            60);

        if (options.CommandTimeoutSeconds >= options.RequestBudgetSeconds)
        {
            failures.Add(
                $"{BookingOptions.SectionName}:CommandTimeoutSeconds must be less than " +
                $"{BookingOptions.SectionName}:RequestBudgetSeconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        ICollection<string> failures,
        string key,
        int value,
        int minimum,
        int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{key} must be between {minimum} and {maximum}.");
        }
    }
}
