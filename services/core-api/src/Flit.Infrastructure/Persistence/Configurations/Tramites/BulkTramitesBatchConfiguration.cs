using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class BulkTramitesBatchConfiguration : IEntityTypeConfiguration<BulkTramitesBatch>
{
    public void Configure(EntityTypeBuilder<BulkTramitesBatch> builder)
    {
        builder.ToTable("bulk_tramites_batches", SchemaNames.Tramites, t =>
            t.HasCheckConstraint("ck_bulk_tramites_batches_total_rows", "total_rows BETWEEN 1 AND 50"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.TemplateType).HasColumnName("template_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.SourceFilename).HasColumnName("source_filename").HasMaxLength(255).IsRequired();
        builder.Property(x => x.TotalRows).HasColumnName("total_rows").IsRequired();
        builder.Property(x => x.RowsWithStructuralErrors).HasColumnName("rows_with_structural_errors").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");

        builder.HasMany(x => x.Rows)
            .WithOne()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Resumen del lote en /tramites (HU #12524): lotes del tenant, más recientes primero.
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_bulk_tramites_batches_tenant_created");
    }
}
