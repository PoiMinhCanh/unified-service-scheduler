using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles", "dbo");
        builder.HasKey(vehicle => vehicle.Id).HasName("PK_Vehicles");
        builder.Property(vehicle => vehicle.Id).ValueGeneratedNever();
        builder.Property(vehicle => vehicle.Vin).HasMaxLength(17).IsRequired();
        builder.Property(vehicle => vehicle.Make).HasMaxLength(100).IsRequired();
        builder.Property(vehicle => vehicle.Model).HasMaxLength(100).IsRequired();

        builder.HasAlternateKey(vehicle => new { vehicle.Id, vehicle.CustomerId })
            .HasName("AK_Vehicles_Id_CustomerId");
        builder.HasIndex(vehicle => vehicle.Vin).IsUnique().HasDatabaseName("UX_Vehicles_Vin");
        builder.HasIndex(vehicle => vehicle.CustomerId).HasDatabaseName("IX_Vehicles_CustomerId");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(vehicle => vehicle.CustomerId)
            .HasConstraintName("FK_Vehicles_Customers_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.Vehicles);
    }
}
