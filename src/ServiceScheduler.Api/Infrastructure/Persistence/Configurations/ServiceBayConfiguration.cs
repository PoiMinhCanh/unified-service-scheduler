using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class ServiceBayConfiguration : IEntityTypeConfiguration<ServiceBay>
{
    public void Configure(EntityTypeBuilder<ServiceBay> builder)
    {
        builder.ToTable("ServiceBays", "dbo");
        builder.HasKey(bay => bay.Id).HasName("PK_ServiceBays");
        builder.Property(bay => bay.Id).ValueGeneratedNever();
        builder.Property(bay => bay.Name).HasMaxLength(200).IsRequired();
        builder.Property(bay => bay.IsActive).HasColumnType("bit");

        builder.HasAlternateKey(bay => new { bay.Id, bay.DealershipId })
            .HasName("AK_ServiceBays_Id_DealershipId");
        builder.HasIndex(bay => new { bay.DealershipId, bay.Name })
            .IsUnique()
            .HasDatabaseName("UX_ServiceBays_DealershipId_Name");

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(bay => bay.DealershipId)
            .HasConstraintName("FK_ServiceBays_Dealerships_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.ServiceBays);
    }
}
