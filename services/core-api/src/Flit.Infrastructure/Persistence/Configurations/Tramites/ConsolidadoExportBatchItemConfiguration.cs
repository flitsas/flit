using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #13368 (ADR-0070) — <c>tramites.consolidado_export_batch_items</c>. Los CHECK (estado, modo de entrega, los diez
/// códigos de omisión de <see cref="ConsolidadoLoteOmisiones"/>, coherencia por estado), las FK a
/// <c>identity.tenants</c> y <c>tramites.procedure_instances</c>, la política RLS decorativa y el trigger de
/// <c>row_version</c> viven en SQL (134-HU13368-consolidado-export-items.sql). Aquí se declaran las FK al lote y a la
/// parte (compuesta, <c>NO ACTION</c>) y los índices, para que el snapshot los refleje.
/// </summary>
internal sealed class ConsolidadoExportBatchItemConfiguration : IEntityTypeConfiguration<ConsolidadoExportBatchItem>
{
    public void Configure(EntityTypeBuilder<ConsolidadoExportBatchItem> builder)
    {
        builder.ToTable("consolidado_export_batch_items", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_consolidado_export_batch_items_row_version"));

        builder.HasKey(x => x.Id).HasName("pk_consolidado_export_batch_items");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        // CF-15: compañía del trámite, también en lotes de Super Admin.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.Position).HasColumnName("position").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text")
            .HasDefaultValue(ConsolidadoExportItemStatus.Pendiente).IsRequired();
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasDefaultValueSql("now()");
        builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.ClaimedBy).HasColumnName("claimed_by").HasColumnType("text");
        builder.Property(x => x.ReferenceNumber).HasColumnName("reference_number").HasColumnType("text").IsRequired();
        builder.Property(x => x.Plate).HasColumnName("plate").HasColumnType("text");
        builder.Property(x => x.AttachmentId).HasColumnName("attachment_id");
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasColumnType("text");
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.DeliveryMode).HasColumnName("delivery_mode").HasColumnType("text");
        builder.Property(x => x.OmissionCode).HasColumnName("omission_code").HasColumnType("text");
        builder.Property(x => x.OmissionReason).HasColumnName("omission_reason").HasColumnType("text");
        builder.Property(x => x.PartNumber).HasColumnName("part_number");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasOne<ConsolidadoExportBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .HasConstraintName("fk_consolidado_export_batch_items_batches")
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION (se verifica al final de la sentencia): el CASCADE desde el lote borra ítems y partes en la misma
        // sentencia sin que el orden importe.
        builder.HasOne<ConsolidadoExportBatchPart>()
            .WithMany()
            .HasForeignKey(x => new { x.BatchId, x.PartNumber })
            .HasPrincipalKey(p => new { p.BatchId, p.PartNumber })
            .HasConstraintName("fk_consolidado_export_batch_items_batch_parts")
            .OnDelete(DeleteBehavior.NoAction);

        // AC4: un trámite no se repite dentro del mismo lote. Cubre además la FK al lote (A9).
        builder.HasIndex(x => new { x.BatchId, x.ProcedureInstanceId })
            .HasDatabaseName("uq_consolidado_export_batch_items_batch_instance")
            .IsUnique();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_consolidado_export_batch_items_tenant");
        builder.HasIndex(x => x.ProcedureInstanceId).HasDatabaseName("ix_consolidado_export_batch_items_procedure_instance");

        // A9 de la FK compuesta + lectura de los ítems de la parte k al empaquetar.
        builder.HasIndex(x => new { x.BatchId, x.PartNumber })
            .HasDatabaseName("ix_consolidado_export_batch_items_batch_part")
            .HasFilter("part_number IS NOT NULL");

        // E4: reclamo dentro del lote (FOR UPDATE SKIP LOCKED).
        builder.HasIndex(x => new { x.BatchId, x.Position })
            .HasDatabaseName("ix_consolidado_export_batch_items_claim")
            .HasFilter("status IN ('pendiente', 'procesando')");

        // E4: asignación de partes (ítems terminados sin parte).
        builder.HasIndex(x => new { x.BatchId, x.ProcessedAt })
            .HasDatabaseName("ix_consolidado_export_batch_items_unassigned")
            .HasFilter("status IN ('incluido', 'omitido') AND part_number IS NULL");
    }
}
