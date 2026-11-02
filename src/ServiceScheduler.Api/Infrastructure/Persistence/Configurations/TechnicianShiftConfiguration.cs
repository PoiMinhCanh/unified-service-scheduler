using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class TechnicianShiftConfiguration : IEntityTypeConfiguration<TechnicianShift>
{
    public void Configure(EntityTypeBuilder<TechnicianShift> builder)
    {
        builder.ToTable("TechnicianShifts", "dbo", table =>
        {
            table.HasCheckConstraint("CK_TechnicianShifts_Interval", "[EndUtc] > [StartUtc]");
            table.HasCheckConstraint(
                "CK_TechnicianShifts_UtcOffsets",
                "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0");
        });

        builder.HasKey(shift => shift.Id).HasName("PK_TechnicianShifts");
        builder.Property(shift => shift.Id).ValueGeneratedNever();
        builder.Property(shift => shift.StartUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(shift => shift.EndUtc).HasColumnType("datetimeoffset(7)");

        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(shift => shift.TechnicianId)
            .HasConstraintName("FK_TechnicianShifts_Technicians_TechnicianId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(shift => new { shift.TechnicianId, shift.StartUtc, shift.EndUtc })
            .IsUnique()
            .HasDatabaseName("UX_TechnicianShifts_TechnicianId_StartUtc_EndUtc");
        builder.HasIndex(shift => new { shift.TechnicianId, shift.StartUtc })
            .HasDatabaseName("IX_TechnicianShifts_TechnicianId_StartUtc")
            .IncludeProperties(shift => new { shift.EndUtc });
    }
}
