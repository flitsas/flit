using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantTransitOfficeGrantConfiguration
    : IEntityTypeConfiguration<TenantTransitOfficeGrant>
{
    public void Configure(EntityTypeBuilder<TenantTransitOfficeGrant> builder)
    {
        builder.ToTable("tenant_transit_office_grants", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_tenant_transit_office_grants");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_transit_office_grants_tenant_id");
    }
}
