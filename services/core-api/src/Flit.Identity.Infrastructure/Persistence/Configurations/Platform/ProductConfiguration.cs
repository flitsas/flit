using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Platform;

/// <summary>
/// Mapeo EF Core del catálogo de productos (HU #12958). El DDL lo gestiona la migración SQL cruda
/// (<c>119-HU12958-platform-products.sql</c>), que también lo siembra: entidad
/// <c>ExcludeFromMigrations</c> (patrón <c>TenantDomainConfiguration</c>).
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<ProductEntity>
{
    public void Configure(EntityTypeBuilder<ProductEntity> builder)
    {
        builder.ToTable("products", SchemaNames.Platform, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Code).HasName("pk_products");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active").IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
    }
}
