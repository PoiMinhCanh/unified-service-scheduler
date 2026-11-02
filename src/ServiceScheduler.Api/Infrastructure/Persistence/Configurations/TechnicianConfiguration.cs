using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("Technicians", "dbo");
        builder.HasKey(technician => technician.Id).HasName("PK_Technicians");
        builder.Property(technician => technician.Id).ValueGeneratedNever();
        builder.Property(technician => technician.Name).HasMaxLength(200).IsRequired();
        builder.Property(technician => technician.IsActive).HasColumnType("bit");

        builder.HasAlternateKey(technician => new { technician.Id, technician.DealershipId })
            .HasName("AK_Technicians_Id_DealershipId");
        builder.HasIndex(technician => technician.DealershipId)
            .HasDatabaseName("IX_Technicians_DealershipId");

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(technician => technician.DealershipId)
            .HasConstraintName("FK_Technicians_Dealerships_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasData(SeedData.Technicians);
    }
}
