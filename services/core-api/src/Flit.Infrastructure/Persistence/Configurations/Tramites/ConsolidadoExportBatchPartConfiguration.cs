using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #13368 (ADR-0070) — <c>tramites.consolidado_export_batch_parts</c>. Los CHECK (estado, binario completo al cerrar,
/// purga, lease, SHA-256), la FK a <c>identity.tenants</c>, la política RLS decorativa y los triggers viven en SQL
/// (134-HU13368-consolidado-export-items.sql). <c>(batch_id, part_number)</c> es clave alterna porque la FK compuesta
/// de los ítems apunta a ella.
/// </summary>
internal sealed class ConsolidadoExportBatchPartConfiguration : IEntityTypeConfiguration<ConsolidadoExportBatchPart>
{
    public void Configure(EntityTypeBuilder<ConsolidadoExportBatchPart> builder)
    {
        builder.ToTable("consolidado_export_batch_parts", SchemaNames.Tramites, t =>
        {
            t.HasTrigger("tr_consolidado_export_batch_parts_tenant");
            t.HasTrigger("tr_consolidado_export_batch_parts_row_version");
        });

        builder.HasKey(x => x.Id).HasName("pk_consolidado_export_batch_parts");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        // E5: lo fija tr_consolidado_export_batch_parts_tenant a partir del lote; EF lo lee de vuelta tras insertar.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").ValueGeneratedOnAdd();
        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.PartNumber).HasColumnName("part_number").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text")
            .HasDefaultValue(ConsolidadoExportPartStatus.Pendiente).IsRequired();
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.PdfCount).HasColumnName("pdf_count");
        builder.Property(x => x.OmittedCount).HasColumnName("omitted_count");
        builder.Property(x => x.PlainSizeBytes).HasColumnName("plain_size_bytes");
        builder.Property(x => x.StoredSizeBytes).HasColumnName("stored_size_bytes");
        builder.Property(x => x.StoredSha256).HasColumnName("stored_sha256").HasColumnType("text");
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasColumnType("text");
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
        builder.Property(x => x.PurgedAt).HasColumnName("purged_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        // Cubre la FK a batches (A9) y es el destino de la FK compuesta de los ítems.
        builder.HasAlternateKey(x => new { x.BatchId, x.PartNumber })
            .HasName("uq_consolidado_export_batch_parts_batch_number");

        builder.HasOne<ConsolidadoExportBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .HasConstraintName("fk_consolidado_export_batch_parts_batches")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_consolidado_export_batch_parts_tenant");

        // E4: carril de empaquetado entre compañías.
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_consolidado_export_batch_parts_claim")
            .HasFilter("status IN ('pendiente', 'empaquetando')");
    }
}
