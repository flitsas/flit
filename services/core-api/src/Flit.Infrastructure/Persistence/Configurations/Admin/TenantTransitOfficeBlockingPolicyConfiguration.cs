using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantTransitOfficeBlockingPolicyConfiguration
    : IEntityTypeConfiguration<TenantTransitOfficeBlockingPolicy>
{
    public void Configure(EntityTypeBuilder<TenantTransitOfficeBlockingPolicy> builder)
    {
        builder.ToTable("tenant_transit_office_blocking_policies", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();

        builder.Property(x => x.Criterion)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Blocks).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Hace atómico el upsert (SetAsync busca por esta tripleta antes de decidir add/update).
        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId, x.Criterion })
            .IsUnique()
            .HasDatabaseName("uq_tenant_transit_office_blocking_policies");

        // Índice general por tenant (tenant_id primero, checklist §A11) — cubre además la query
        // caliente del preflight por (tenant_id, transit_office_id).
        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .HasDatabaseName("ix_tenant_transit_office_blocking_policies_tenant_office");
    }
}
