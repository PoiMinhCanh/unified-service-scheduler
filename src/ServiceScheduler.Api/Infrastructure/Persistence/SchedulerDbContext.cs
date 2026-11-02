using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence;

/// <summary>
/// Persistence boundary for the shared scheduler domain. Reads use EF Core directly; confirmed
/// appointment writes will be restricted to a same-owner procedure in ticket 3.
/// </summary>
public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<Dealership> Dealerships => Set<Dealership>();

    public DbSet<TimeZoneMapping> TimeZoneMappings => Set<TimeZoneMapping>();

    public DbSet<DealershipOpeningHour> DealershipOpeningHours => Set<DealershipOpeningHour>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<TechnicianQualification> TechnicianQualifications => Set<TechnicianQualification>();

    public DbSet<TechnicianShift> TechnicianShifts => Set<TechnicianShift>();

    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulerDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The schema contract selects an explicit index inventory. Disable EF's convention FK
        // indexes so the migration contains no indexes beyond that inventory.
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }
}
