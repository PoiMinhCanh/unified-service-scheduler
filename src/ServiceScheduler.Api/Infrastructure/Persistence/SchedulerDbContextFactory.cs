using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServiceScheduler.Api.Infrastructure.Persistence;

/// <summary>
/// Builds the model for design-time migration commands. It never opens a connection, so generating
/// a migration does not require a live database or a real setup password.
/// </summary>
public sealed class SchedulerDbContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    private const string PlaceholderConnectionString =
        "Server=localhost,1433;Database=ServiceScheduler;User Id=sa;Password=DesignTimeOnly1!;Encrypt=True;TrustServerCertificate=True;Connect Timeout=3";

    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Setup");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SchedulerDbContext(options);
    }
}
