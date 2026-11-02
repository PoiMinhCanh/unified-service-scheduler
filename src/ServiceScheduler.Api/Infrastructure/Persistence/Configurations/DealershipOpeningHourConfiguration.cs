using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class DealershipOpeningHourConfiguration : IEntityTypeConfiguration<DealershipOpeningHour>
{
    public void Configure(EntityTypeBuilder<DealershipOpeningHour> builder)
    {
        builder.ToTable("DealershipOpeningHours", "dbo", table =>
        {
            table.HasCheckConstraint(
                "CK_DealershipOpeningHours_DayOfWeek",
                "[DayOfWeek] BETWEEN 0 AND 6");
            table.HasCheckConstraint(
                "CK_DealershipOpeningHours_Interval",
                "[ClosesAt] > [OpensAt]");
        });

        builder.HasKey(hours => new { hours.DealershipId, hours.DayOfWeek, hours.OpensAt })
            .HasName("PK_DealershipOpeningHours");
        builder.Property(hours => hours.DayOfWeek).HasColumnType("tinyint");
        builder.Property(hours => hours.OpensAt).HasColumnType("time(0)");
        builder.Property(hours => hours.ClosesAt).HasColumnType("time(0)");

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(hours => hours.DealershipId)
            .HasConstraintName("FK_DealershipOpeningHours_Dealerships_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.OpeningHours);
    }
}
