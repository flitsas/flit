using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>
/// Mapea <c>analytics.export_jobs</c> (Feature #11076, ADR-0037).
/// Checklist §B: B9 (IEntityTypeConfiguration), B8 (row_version concurrency token).
/// </summary>
internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("export_jobs", SchemaNames.Analytics, t =>
        {
            t.HasTrigger("tr_export_jobs_row_version");
            t.HasTrigger("tr_export_jobs_audit");
            t.HasTrigger("tr_export_jobs_notify");
            t.HasTrigger("tr_export_jobs_pending_limit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OwnerUserId).IsRequired();

        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("pending");
        builder.Property(x => x.ReportType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Format).HasMaxLength(10).IsRequired();

        builder.Property(x => x.FiltersJson)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        builder.Property(x => x.ProgressPct).IsRequired().HasDefaultValue((short)0);
        builder.Property(x => x.CorrelationId);
        builder.Property(x => x.FileStoragePath).HasMaxLength(500);
        builder.Property(x => x.FileSizeBytes);
        builder.Property(x => x.FileSha256).HasMaxLength(64);
        builder.Property(x => x.ErrorMessage);
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.StartedAt);
        builder.Property(x => x.CompletedAt);

        // B8: row_version como token de concurrencia (optimistic locking)
        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy);

        // A11: tenant_id primera columna en índice compuesto
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId })
            .HasDatabaseName("ix_export_jobs_tenant_owner")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_export_jobs_status_created")
            .HasFilter("status = 'pending' AND deleted_at IS NULL");

        builder.HasIndex(x => x.OwnerUserId)
            .HasDatabaseName("ix_export_jobs_owner_user_id");
    }
}
