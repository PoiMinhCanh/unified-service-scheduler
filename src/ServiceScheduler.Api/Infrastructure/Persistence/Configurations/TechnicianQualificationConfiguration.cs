using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class TechnicianQualificationConfiguration : IEntityTypeConfiguration<TechnicianQualification>
{
    public void Configure(EntityTypeBuilder<TechnicianQualification> builder)
    {
        builder.ToTable("TechnicianQualifications", "dbo");
        builder.HasKey(qualification => new { qualification.TechnicianId, qualification.ServiceTypeId })
            .HasName("PK_TechnicianQualifications");

        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(qualification => new { qualification.TechnicianId, qualification.DealershipId })
            .HasPrincipalKey(technician => new { technician.Id, technician.DealershipId })
            .HasConstraintName("FK_TechnicianQualifications_Technicians_TechnicianId_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ServiceType>()
            .WithMany()
            .HasForeignKey(qualification => new { qualification.ServiceTypeId, qualification.DealershipId })
            .HasPrincipalKey(serviceType => new { serviceType.Id, serviceType.DealershipId })
            .HasConstraintName("FK_TechnicianQualifications_ServiceTypes_ServiceTypeId_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(qualification => new { qualification.ServiceTypeId, qualification.DealershipId })
            .HasDatabaseName("IX_TechnicianQualifications_ServiceTypeId_DealershipId");

        builder.HasData(SeedData.Qualifications);
    }
}
