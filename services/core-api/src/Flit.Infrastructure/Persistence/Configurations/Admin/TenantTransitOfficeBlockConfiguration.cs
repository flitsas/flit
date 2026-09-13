using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantTransitOfficeBlockConfiguration
    : IEntityTypeConfiguration<TenantTransitOfficeBlock>
{
    public void Configure(EntityTypeBuilder<TenantTransitOfficeBlock> builder)
    {
        builder.ToTable("tenant_transit_office_blocks", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_tenant_transit_office_blocks");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_transit_office_blocks_tenant_id");
    }
}
