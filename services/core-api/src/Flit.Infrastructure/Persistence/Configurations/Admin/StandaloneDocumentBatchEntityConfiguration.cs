using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de <c>admin.standalone_document_batches</c> (Feature #12201, I3). El DDL lo gestiona
/// la migración SQL cruda (<c>106-F12201-generacion-documental-lotes.sql</c>): CHECKs, índices
/// parciales, RLS y los dos triggers. Entidad <c>ExcludeFromMigrations</c>, mismo patrón que
/// <see cref="StandaloneDocumentEntityConfiguration"/>.
/// </summary>
internal sealed class StandaloneDocumentBatchEntityConfiguration
    : IEntityTypeConfiguration<StandaloneDocumentBatchEntity>
{
    public void Configure(EntityTypeBuilder<StandaloneDocumentBatchEntity> builder)
    {
        builder.ToTable("standalone_document_batches", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_standalone_document_batches_row_version");
            t.HasTrigger("tr_standalone_document_batches_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();

        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.TemplateVersion)
            .HasColumnName("template_version").HasMaxLength(10).IsRequired();

        builder.Property(x => x.SourceFilename)
            .HasColumnName("source_filename").HasMaxLength(500).IsRequired();
        builder.Property(x => x.SourceStoragePath)
            .HasColumnName("source_storage_path").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceSha256)
            .HasColumnName("source_sha256").HasMaxLength(64).IsRequired();

        builder.Property(x => x.TotalItems).HasColumnName("total_items").IsRequired();
        builder.Property(x => x.GeneratedCount).HasColumnName("generated_count").IsRequired();
        builder.Property(x => x.ErrorCount).HasColumnName("error_count").IsRequired();

        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);

        builder.Property(x => x.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_standalone_document_batches_tenant_created");
        builder.HasIndex(x => new { x.TenantId, x.CreatedByUserId })
            .HasDatabaseName("ix_standalone_document_batches_tenant_user");
    }
}
