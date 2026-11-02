using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class TimeZoneMappingConfiguration : IEntityTypeConfiguration<TimeZoneMapping>
{
    public void Configure(EntityTypeBuilder<TimeZoneMapping> builder)
    {
        builder.ToTable("TimeZoneMappings", "dbo");
        builder.HasKey(mapping => mapping.IanaId).HasName("PK_TimeZoneMappings");
        builder.Property(mapping => mapping.IanaId).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(mapping => mapping.WindowsId).HasMaxLength(100).IsRequired();
        builder.HasIndex(mapping => mapping.WindowsId)
            .IsUnique()
            .HasDatabaseName("UX_TimeZoneMappings_WindowsId");

        builder.HasData(SeedData.TimeZoneMappings);
    }
}
