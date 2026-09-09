using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de <c>admin.standalone_documents</c> (Feature #12201,
/// ADR-0056-generacion-documental-standalone). El DDL lo gestiona la migración SQL cruda
/// (<c>105-F12201-generacion-documental.sql</c>): CHECKs, índices parciales, RLS y los tres
/// triggers. Entidad <c>ExcludeFromMigrations</c>, patrón del baúl y de las escrituras.
///
/// <para>Los nombres de columna críticos se declaran EXPLÍCITAMENTE aunque la convención
/// snake_case los produciría igual: <c>created_by_user_id</c>, <c>scenario</c> y
/// <c>storage_sha256</c> son los que el DDL fija y confundirlos (p. ej. con <c>flit_user_id</c> o
/// <c>sha256</c>, que existen en OTRAS tablas del schema) falla en runtime, no al compilar.</para>
/// </summary>
internal sealed class StandaloneDocumentEntityConfiguration
    : IEntityTypeConfiguration<StandaloneDocumentEntity>
{
    public void Configure(EntityTypeBuilder<StandaloneDocumentEntity> builder)
    {
        builder.ToTable("standalone_documents", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_standalone_documents_row_version");
            t.HasTrigger("tr_standalone_documents_audit");
            t.HasTrigger("tr_standalone_documents_immutable");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();

        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Scenario).HasColumnName("scenario").HasMaxLength(1);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(60);
        builder.Property(x => x.ErrorField).HasColumnName("error_field").HasMaxLength(120);

        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasMaxLength(1000);
        builder.Property(x => x.StorageSha256).HasColumnName("storage_sha256").HasMaxLength(64);
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(500);

        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);

        builder.Property(x => x.InputSummary)
            .HasColumnName("input_summary").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.RuesSnapshot).HasColumnName("rues_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.DocumentSnapshot).HasColumnName("document_snapshot").HasColumnType("jsonb");

        builder.Property(x => x.DownloadedAt).HasColumnName("downloaded_at");
        builder.Property(x => x.DownloadCount).HasColumnName("download_count").IsRequired();

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_standalone_documents_tenant_created");
        builder.HasIndex(x => new { x.TenantId, x.CreatedByUserId })
            .HasDatabaseName("ix_standalone_documents_tenant_user");
    }
}
