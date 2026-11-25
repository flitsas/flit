using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Platform;

/// <summary>
/// Mapeo EF Core de <c>platform.tenant_product_provisioning</c> (HU #14694, ADR-0093). El DDL
/// (<c>205-impuestos-producto-y-aprovisionamiento.sql</c>) lleva la unicidad por empresa y producto, las FK, los CHECK,
/// RLS y los triggers (row_version + audit). Entidad <c>ExcludeFromMigrations</c>; la aplican los dos contextos
/// (<c>FlitDbContext</c> e <c>IdentityDbContext</c>).
/// </summary>
internal sealed class TenantProductProvisioningConfiguration : IEntityTypeConfiguration<TenantProductProvisioningEntity>
{
    public void Configure(EntityTypeBuilder<TenantProductProvisioningEntity> builder)
    {
        builder.ToTable("tenant_product_provisioning", SchemaNames.Platform, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_product_provisioning_row_version");
            t.HasTrigger("tr_tenant_product_provisioning_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_product_provisioning");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Estado).HasColumnName("estado").HasMaxLength(32).IsRequired();
        builder.Property(x => x.MotivoCodigo).HasColumnName("motivo_codigo").HasMaxLength(64);
        builder.Property(x => x.Mensaje).HasColumnName("mensaje").HasMaxLength(300);
        builder.Property(x => x.Accion).HasColumnName("accion").HasMaxLength(32);
        builder.Property(x => x.FirmaConfirmada).HasColumnName("firma_confirmada");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.ActualizadoEn).HasColumnName("actualizado_en").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.ProductCode }, "uq_tenant_product_provisioning_tenant_product")
            .HasDatabaseName("uq_tenant_product_provisioning_tenant_product")
            .IsUnique();
        builder.HasIndex(x => x.ProductCode, "ix_tenant_product_provisioning_product_code")
            .HasDatabaseName("ix_tenant_product_provisioning_product_code");
    }
}
