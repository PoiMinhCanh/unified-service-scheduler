using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Features.Availability;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

public sealed class AvailabilitySqlTests
{
    private static readonly DateTimeOffset StartsAtUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset EndsAtUtc = new(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceLookupIsDealershipScoped()
    {
        using var dbContext = CreateDbContext();

        var sql = AvailabilityQueries
            .ServiceLookups(dbContext, SeedData.DealershipIds.Saigon, SeedData.ServiceTypeIds.SaigonOilChange)
            .ToQueryString();

        Assert.Contains("[ServiceTypes]", sql, StringComparison.Ordinal);
        Assert.Contains("[Dealerships]", sql, StringComparison.Ordinal);
        Assert.Contains("[DurationMinutes]", sql, StringComparison.Ordinal);
        Assert.Contains("[TimeZoneId]", sql, StringComparison.Ordinal);
        Assert.Contains("[Id] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[DealershipId] = @", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningCoverageRequiresOneRowForTheLocalInterval()
    {
        using var dbContext = CreateDbContext();

        var sql = AvailabilityQueries
            .OpeningStarts(
                dbContext,
                SeedData.DealershipIds.Saigon,
                2,
                new TimeOnly(10, 0),
                new TimeOnly(11, 0))
            .ToQueryString();

        Assert.Contains("[DealershipOpeningHours]", sql, StringComparison.Ordinal);
        Assert.Contains("[DayOfWeek]", sql, StringComparison.Ordinal);
        Assert.Contains("[DealershipId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[OpensAt] <= @", sql, StringComparison.Ordinal);
        Assert.Contains("[ClosesAt] >= @", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicianCandidatesUseShiftCoverageAndHalfOpenConflictPredicates()
    {
        using var dbContext = CreateDbContext();

        var sql = AvailabilityQueries
            .TechnicianCandidates(
                dbContext,
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                StartsAtUtc,
                EndsAtUtc)
            .ToQueryString();

        Assert.Contains("[Technicians]", sql, StringComparison.Ordinal);
        Assert.Contains("[TechnicianQualifications]", sql, StringComparison.Ordinal);
        Assert.Contains("[TechnicianShifts]", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Status]", sql, StringComparison.Ordinal);
        Assert.Contains("[DealershipId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[ServiceTypeId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[IsActive] = CAST(1 AS bit)", sql, StringComparison.Ordinal);
        Assert.Contains("[StartUtc] <= @", sql, StringComparison.Ordinal);
        Assert.Contains("[EndUtc] >= @", sql, StringComparison.Ordinal);
        Assert.Contains("[StartUtc] < @", sql, StringComparison.Ordinal);
        Assert.Contains("[EndUtc] > @", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BayCandidatesUseIndependentHalfOpenConflictPredicates()
    {
        using var dbContext = CreateDbContext();

        var sql = AvailabilityQueries
            .BayCandidates(
                dbContext,
                SeedData.DealershipIds.Saigon,
                StartsAtUtc,
                EndsAtUtc)
            .ToQueryString();

        Assert.Contains("[ServiceBays]", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[DealershipId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[ServiceBayId] = [", sql, StringComparison.Ordinal);
        Assert.Contains("[IsActive] = CAST(1 AS bit)", sql, StringComparison.Ordinal);
        Assert.Contains("[StartUtc] < @", sql, StringComparison.Ordinal);
        Assert.Contains("[EndUtc] > @", sql, StringComparison.Ordinal);
        Assert.Contains("[Status]", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(
                "Server=127.0.0.1,1;Database=ServiceScheduler;User Id=scheduler_app;" +
                "Password=TestOnly1!;TrustServerCertificate=True;Connect Timeout=1")
            .Options;

        return new SchedulerDbContext(options);
    }
}
