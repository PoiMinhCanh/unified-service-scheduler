using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>
/// Validates cache configuration without opening a Redis connection. Reachability is a runtime
/// concern so that an unavailable cache never prevents the API from starting.
/// </summary>
public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    private static readonly TimeSpan MaximumOperationTimeout = TimeSpan.FromSeconds(5);

    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            errors.Add($"{CacheOptions.ConnectionStringKey} is required.");
        }
        else
        {
            ValidateConnectionString(options.ConnectionString, errors);
        }

        if (options.ServiceCatalog is null || options.ServiceCatalog.AbsoluteTtl <= TimeSpan.Zero)
        {
            errors.Add($"{CacheOptions.SectionName}:ServiceCatalog:AbsoluteTtl must be a positive duration.");
        }

        if (options.OperationTimeout <= TimeSpan.Zero || options.OperationTimeout > MaximumOperationTimeout)
        {
            errors.Add($"{CacheOptions.SectionName}:OperationTimeout must be positive and at most {MaximumOperationTimeout.TotalSeconds} seconds.");
        }

        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            errors.Add($"{CacheOptions.SectionName}:ConnectTimeout must be a positive duration.");
        }

        if (options.CommandTimeout <= TimeSpan.Zero)
        {
            errors.Add($"{CacheOptions.SectionName}:CommandTimeout must be a positive duration.");
        }

        if (options.ConnectRetry < 0)
        {
            errors.Add($"{CacheOptions.SectionName}:ConnectRetry must not be negative.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateConnectionString(string connectionString, ICollection<string> errors)
    {
        ConfigurationOptions configuration;
        try
        {
            configuration = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            errors.Add($"{CacheOptions.ConnectionStringKey} is not a valid Redis connection string.");
            return;
        }

        if (configuration.EndPoints.Count == 0)
        {
            errors.Add($"{CacheOptions.ConnectionStringKey} must specify at least one Redis endpoint.");
        }
    }
}
