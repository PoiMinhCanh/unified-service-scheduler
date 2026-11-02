using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes", "dbo", table =>
        {
            table.HasCheckConstraint("CK_ServiceTypes_DurationMinutes", "[DurationMinutes] > 0");
        });

        builder.HasKey(serviceType => serviceType.Id).HasName("PK_ServiceTypes");
        builder.Property(serviceType => serviceType.Id).ValueGeneratedNever();
        builder.Property(serviceType => serviceType.Name).HasMaxLength(200).IsRequired();
        builder.Property(serviceType => serviceType.DurationMinutes).HasColumnType("int");

        builder.HasAlternateKey(serviceType => new { serviceType.Id, serviceType.DealershipId })
            .HasName("AK_ServiceTypes_Id_DealershipId");
        builder.HasIndex(serviceType => new { serviceType.DealershipId, serviceType.Name })
            .IsUnique()
            .HasDatabaseName("UX_ServiceTypes_DealershipId_Name");

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(serviceType => serviceType.DealershipId)
            .HasConstraintName("FK_ServiceTypes_Dealerships_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.ServiceTypes);
    }
}
