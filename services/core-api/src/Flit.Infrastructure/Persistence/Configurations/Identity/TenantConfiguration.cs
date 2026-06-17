using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Identity;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", SchemaNames.Identity);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_tenants_code");

        builder.Property(x => x.LegalName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TaxId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TenantType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
    }
}
