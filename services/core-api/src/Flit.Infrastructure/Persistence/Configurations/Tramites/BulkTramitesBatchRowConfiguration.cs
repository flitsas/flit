using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class BulkTramitesBatchRowConfiguration : IEntityTypeConfiguration<BulkTramitesBatchRow>
{
    public void Configure(EntityTypeBuilder<BulkTramitesBatchRow> builder)
    {
        builder.ToTable("bulk_tramites_batch_rows", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.RowNumber).HasColumnName("row_number").IsRequired();
        builder.Property(x => x.ValuesJson).HasColumnName("values").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.StructuralErrorCode).HasColumnName("structural_error_code").HasMaxLength(50);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(20);
        builder.Property(x => x.OutcomeReason).HasColumnName("outcome_reason").HasMaxLength(500);
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.BatchId, x.RowNumber })
            .IsUnique()
            .HasDatabaseName("uq_bulk_tramites_batch_rows_batch_row");

        // Cola de HU #12523: filas sin error estructural y aún no procesadas.
        builder.HasIndex(x => x.BatchId)
            .HasDatabaseName("ix_bulk_tramites_batch_rows_pending")
            .HasFilter("structural_error_code IS NULL AND outcome IS NULL");
    }
}
