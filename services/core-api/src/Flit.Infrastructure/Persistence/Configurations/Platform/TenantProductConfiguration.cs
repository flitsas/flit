using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Platform;

/// <summary>
/// Mapeo EF Core de la habilitación de productos por empresa (HU #12958, contrato v1 §4). El DDL
/// (<c>119-HU12958-platform-products.sql</c>) lleva la unicidad por empresa y producto, las FK, el
/// CHECK que excluye <c>plataforma</c>, RLS y los triggers (row_version + audit). Entidad
/// <c>ExcludeFromMigrations</c>.
/// </summary>
internal sealed class TenantProductConfiguration : IEntityTypeConfiguration<TenantProductEntity>
{
    public void Configure(EntityTypeBuilder<TenantProductEntity> builder)
    {
        builder.ToTable("tenant_products", SchemaNames.Platform, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_products_row_version");
            t.HasTrigger("tr_tenant_products_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_products");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.ProductCode }, "uq_tenant_products_tenant_product")
            .HasDatabaseName("uq_tenant_products_tenant_product")
            .IsUnique();
        builder.HasIndex(x => x.ProductCode, "ix_tenant_products_product_code")
            .HasDatabaseName("ix_tenant_products_product_code");
    }
}
