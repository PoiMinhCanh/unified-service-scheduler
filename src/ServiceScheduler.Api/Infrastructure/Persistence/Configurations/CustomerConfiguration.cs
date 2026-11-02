using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceScheduler.Api.Infrastructure.Persistence.Entities;

namespace ServiceScheduler.Api.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", "dbo");
        builder.HasKey(customer => customer.Id).HasName("PK_Customers");
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
        builder.Property(customer => customer.Email).HasMaxLength(256).IsRequired();
        builder.HasData(SeedData.Customers);
    }
}
