using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", "dbo", table =>
        {
            table.HasCheckConstraint("CK_Appointments_DurationMinutes", "[DurationMinutes] > 0");
            table.HasCheckConstraint("CK_Appointments_Interval", "[EndUtc] > [StartUtc]");
            table.HasCheckConstraint(
                "CK_Appointments_DurationSnapshot",
                "[EndUtc] = DATEADD(minute, [DurationMinutes], [StartUtc])");
            table.HasCheckConstraint("CK_Appointments_Status", "[Status] IN (N'Confirmed')");
            table.HasCheckConstraint(
                "CK_Appointments_UtcOffsets",
                "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedUtc]) = 0");
        });

        builder.HasKey(appointment => appointment.Id).HasName("PK_Appointments");
        builder.Property(appointment => appointment.Id).ValueGeneratedNever();
        builder.Property(appointment => appointment.StartUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(appointment => appointment.EndUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(appointment => appointment.DurationMinutes).HasColumnType("int");
        builder.Property(appointment => appointment.Status).HasMaxLength(20).IsRequired();
        builder.Property(appointment => appointment.CreatedUtc).HasColumnType("datetimeoffset(7)");

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(appointment => appointment.DealershipId)
            .HasConstraintName("FK_Appointments_Dealerships_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(appointment => appointment.CustomerId)
            .HasConstraintName("FK_Appointments_Customers_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(appointment => new { appointment.VehicleId, appointment.CustomerId })
            .HasPrincipalKey(vehicle => new { vehicle.Id, vehicle.CustomerId })
            .HasConstraintName("FK_Appointments_Vehicles_VehicleId_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ServiceType>()
            .WithMany()
            .HasForeignKey(appointment => new { appointment.ServiceTypeId, appointment.DealershipId })
            .HasPrincipalKey(serviceType => new { serviceType.Id, serviceType.DealershipId })
            .HasConstraintName("FK_Appointments_ServiceTypes_ServiceTypeId_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Technician>()
            .WithMany()
            .HasForeignKey(appointment => new { appointment.TechnicianId, appointment.DealershipId })
            .HasPrincipalKey(technician => new { technician.Id, technician.DealershipId })
            .HasConstraintName("FK_Appointments_Technicians_TechnicianId_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ServiceBay>()
            .WithMany()
            .HasForeignKey(appointment => new { appointment.ServiceBayId, appointment.DealershipId })
            .HasPrincipalKey(bay => new { bay.Id, bay.DealershipId })
            .HasConstraintName("FK_Appointments_ServiceBays_ServiceBayId_DealershipId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(appointment => appointment.CustomerId)
            .HasDatabaseName("IX_Appointments_CustomerId");
        builder.HasIndex(appointment => new { appointment.VehicleId, appointment.CustomerId })
            .HasDatabaseName("IX_Appointments_VehicleId_CustomerId");
        builder.HasIndex(appointment => new { appointment.ServiceTypeId, appointment.DealershipId })
            .HasDatabaseName("IX_Appointments_ServiceTypeId_DealershipId");
        builder.HasIndex(appointment => new { appointment.DealershipId, appointment.TechnicianId, appointment.StartUtc })
            .HasDatabaseName("IX_Appointments_DealershipId_TechnicianId_StartUtc")
            .IncludeProperties(appointment => new { appointment.EndUtc, appointment.Status });
        builder.HasIndex(appointment => new { appointment.DealershipId, appointment.ServiceBayId, appointment.StartUtc })
            .HasDatabaseName("IX_Appointments_DealershipId_ServiceBayId_StartUtc")
            .IncludeProperties(appointment => new { appointment.EndUtc, appointment.Status });
    }
}
