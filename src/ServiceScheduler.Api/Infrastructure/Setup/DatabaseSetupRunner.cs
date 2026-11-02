using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;

namespace ServiceScheduler.Api.Infrastructure.Setup;

/// <summary>
/// Runs the privileged initialization sequence used by --db-setup and by SQL integration fixtures:
/// migrations, runtime principal provisioning, then time-relative shift seeding.
/// </summary>
public sealed class DatabaseSetupRunner(ILogger<DatabaseSetupRunner> logger)
{
    public async Task RunAsync(
        SchedulerSetupOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        logger.LogInformation("Applying database migrations.");
        var dbContextOptions = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(options.SetupConnectionString)
            .Options;

        await using var dbContext = new SchedulerDbContext(dbContextOptions);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Validating SQL time-zone mappings and the booking procedure.");
        var validator = new TimeZoneMappingValidator(options.SetupConnectionString);
        await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Provisioning the restricted runtime principal.");
        var provisioner = new RuntimePrincipalProvisioner(options.SetupConnectionString);
        await provisioner.ProvisionAsync(options.RuntimePassword, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeding future technician shifts.");
        var seeder = new FutureShiftSeeder(dbContext, timeProvider);
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Database setup completed.");
    }
}
