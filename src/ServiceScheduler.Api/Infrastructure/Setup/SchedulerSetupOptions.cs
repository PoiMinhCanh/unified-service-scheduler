using Microsoft.Data.SqlClient;

namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Setup-mode configuration. These values are read only by the explicit --db-setup path and are
/// never registered with or used by the API host.
/// </summary>
public sealed class SchedulerSetupOptions
{
    public const string SetupConnectionKey = "ConnectionStrings:Setup";
    public const string RuntimePasswordKey = "Scheduler:RuntimePassword";
    public const string RuntimeLoginName = "scheduler_app";

    public required string SetupConnectionString { get; init; }

    public required string RuntimePassword { get; init; }

    public static SchedulerSetupOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var setupConnectionString = configuration.GetConnectionString("Setup");
        var runtimePassword = configuration[RuntimePasswordKey];
        var errors = Validate(setupConnectionString, runtimePassword);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        return new SchedulerSetupOptions
        {
            SetupConnectionString = setupConnectionString!,
            RuntimePassword = runtimePassword!,
        };
    }

    /// <summary>
    /// Validates setup configuration without opening a database connection. Messages name the
    /// configuration key and never include a credential value.
    /// </summary>
    public static IReadOnlyList<string> Validate(string? setupConnectionString, string? runtimePassword)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(setupConnectionString))
        {
            errors.Add($"{SetupConnectionKey} is required.");
        }
        else
        {
            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(setupConnectionString);
            }
            catch (ArgumentException)
            {
                return [$"{SetupConnectionKey} is not a valid SQL Server connection string."];
            }

            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                errors.Add($"{SetupConnectionKey} must specify a server.");
            }

            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                errors.Add($"{SetupConnectionKey} must specify a database.");
            }

            if (string.IsNullOrWhiteSpace(builder.UserID))
            {
                errors.Add($"{SetupConnectionKey} must specify a SQL user.");
            }

            if (string.IsNullOrWhiteSpace(builder.Password))
            {
                errors.Add($"{SetupConnectionKey} must specify a password.");
            }

            if (string.Equals(builder.UserID, RuntimeLoginName, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{SetupConnectionKey} must use a privileged setup identity, not the runtime identity.");
            }

            if (builder.ConnectTimeout <= 0)
            {
                errors.Add($"{SetupConnectionKey} must specify a finite, positive connection timeout.");
            }
        }

        if (string.IsNullOrWhiteSpace(runtimePassword))
        {
            errors.Add($"{RuntimePasswordKey} is required.");
        }
        else if (runtimePassword.Length < 8)
        {
            errors.Add($"{RuntimePasswordKey} must be at least 8 characters.");
        }

        return errors;
    }
}
