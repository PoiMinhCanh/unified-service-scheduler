using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class DealershipConfiguration : IEntityTypeConfiguration<Dealership>
{
    public void Configure(EntityTypeBuilder<Dealership> builder)
    {
        builder.ToTable("Dealerships", "dbo");
        builder.HasKey(dealership => dealership.Id).HasName("PK_Dealerships");
        builder.Property(dealership => dealership.Id).ValueGeneratedNever();
        builder.Property(dealership => dealership.Name).HasMaxLength(200).IsRequired();
        builder.Property(dealership => dealership.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.HasIndex(dealership => dealership.Name).IsUnique().HasDatabaseName("UX_Dealerships_Name");

        builder.HasOne<TimeZoneMapping>()
            .WithMany()
            .HasForeignKey(dealership => dealership.TimeZoneId)
            .HasConstraintName("FK_Dealerships_TimeZoneMappings_TimeZoneId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.Dealerships);
    }
}
