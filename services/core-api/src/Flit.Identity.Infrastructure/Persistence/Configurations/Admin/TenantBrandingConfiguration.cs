using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la identidad de marca (HU #12412, ADR-0060). El DDL lo gestiona la migración SQL
/// cruda (<c>115-HU12412-tenant-brandings.sql</c>): CHECKs de forma jsonb y de coherencia de
/// publicación, disparador de tipo MARCA_BLANCA, RLS y triggers (row_version + audit). Entidad
/// <c>ExcludeFromMigrations</c> (patrón <c>CompanyPersonalizedDocumentConfiguration</c>).
/// </summary>
internal sealed class TenantBrandingConfiguration : IEntityTypeConfiguration<TenantBrandingEntity>
{
    public void Configure(EntityTypeBuilder<TenantBrandingEntity> builder)
    {
        builder.ToTable("tenant_brandings", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_brandings_marca_blanca");
            t.HasTrigger("tr_tenant_brandings_row_version");
            t.HasTrigger("tr_tenant_brandings_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_brandings");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Draft).HasColumnName("draft").HasColumnType("jsonb")
            .HasDefaultValueSql("'{\"schemaVersion\":1}'::jsonb").IsRequired();
        builder.Property(x => x.Published).HasColumnName("published").HasColumnType("jsonb");
        builder.Property(x => x.PublishedVersion).HasColumnName("published_version").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.PublishedBy).HasColumnName("published_by");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        // Refleja uq_tenant_brandings_tenant_id del DDL (una marca por cabeza); no lo crea.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("uq_tenant_brandings_tenant_id");
    }
}
