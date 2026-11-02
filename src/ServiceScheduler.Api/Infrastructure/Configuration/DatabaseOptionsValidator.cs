using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ServiceScheduler.Api.Infrastructure.Configuration;

/// <summary>
/// Validates the runtime connection string without opening a database connection. Reachability
/// belongs to the readiness check so that a database outage never prevents the process from starting.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public const string RuntimeKey = "ConnectionStrings:Runtime";

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Runtime))
        {
            return ValidateOptionsResult.Fail($"{RuntimeKey} is required.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(options.Runtime);
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail($"{RuntimeKey} is not a valid SQL Server connection string.");
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            errors.Add($"{RuntimeKey} must specify a server.");
        }

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            errors.Add($"{RuntimeKey} must specify a database.");
        }

        if (string.IsNullOrWhiteSpace(builder.UserID))
        {
            errors.Add($"{RuntimeKey} must specify a SQL user.");
        }

        if (string.IsNullOrWhiteSpace(builder.Password))
        {
            errors.Add($"{RuntimeKey} must specify a password.");
        }

        if (string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{RuntimeKey} must use the restricted runtime identity, not 'sa'.");
        }

        if (builder.ConnectTimeout <= 0)
        {
            errors.Add($"{RuntimeKey} must specify a finite, positive connection timeout.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
