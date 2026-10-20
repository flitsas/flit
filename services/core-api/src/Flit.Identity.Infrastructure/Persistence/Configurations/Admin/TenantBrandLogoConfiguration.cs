using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la versión del logotipo de marca (HU #12412 AC7, ADR-0060). El DDL lo gestiona la
/// migración SQL cruda (<c>115-HU12412-tenant-brandings.sql</c>): CHECKs de vocabulario, tamaño,
/// dimensiones y hash, único parcial de una sola versión <c>active</c> por cabeza, disparador de tipo
/// MARCA_BLANCA, RLS y triggers (row_version + audit). Entidad <c>ExcludeFromMigrations</c>.
/// </summary>
internal sealed class TenantBrandLogoConfiguration : IEntityTypeConfiguration<TenantBrandLogoEntity>
{
    public void Configure(EntityTypeBuilder<TenantBrandLogoEntity> builder)
    {
        builder.ToTable("tenant_brand_logos", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_brand_logos_marca_blanca");
            t.HasTrigger("tr_tenant_brand_logos_row_version");
            t.HasTrigger("tr_tenant_brand_logos_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_brand_logos");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active").IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.StorageSha256).HasColumnName("storage_sha256").HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.WidthPx).HasColumnName("width_px").IsRequired();
        builder.Property(x => x.HeightPx).HasColumnName("height_px").IsRequired();
        builder.Property(x => x.SupersededAt).HasColumnName("superseded_at");
        builder.Property(x => x.SupersededBy).HasColumnName("superseded_by");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        // Reflejan los índices del DDL; no los crean (ExcludeFromMigrations).
        builder.HasIndex(x => new { x.TenantId, x.Version })
            .IsUnique()
            .HasDatabaseName("uq_tenant_brand_logos_tenant_version");

        // Dos índices sobre la misma columna: EF solo los conserva ambos si llevan nombre de modelo.
        builder.HasIndex(x => x.TenantId, "uq_tenant_brand_logos_one_active")
            .IsUnique()
            .HasDatabaseName("uq_tenant_brand_logos_one_active")
            .HasFilter("status = 'active' AND deleted_at IS NULL");

        builder.HasIndex(x => x.TenantId, "ix_tenant_brand_logos_tenant_id")
            .HasDatabaseName("ix_tenant_brand_logos_tenant_id");
    }
}
