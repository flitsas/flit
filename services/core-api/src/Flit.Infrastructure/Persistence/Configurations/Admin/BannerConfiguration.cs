using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.ToTable("banners", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_banners_row_version");
            t.HasTrigger("tr_banners_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ImageStoragePath).HasColumnName("image_storage_path").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ImageSha256).HasColumnName("image_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.LinkUrl).HasColumnName("link_url").HasMaxLength(2048);
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from");
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        // Refleja el indice parcial del DDL (ix_banners_activo_vigencia); no lo crea (la
        // entidad esta ExcludeFromMigrations).
        builder.HasIndex(x => new { x.IsActive, x.ValidFrom, x.ValidUntil })
            .HasDatabaseName("ix_banners_activo_vigencia")
            .HasFilter("deleted_at IS NULL");
    }
}
