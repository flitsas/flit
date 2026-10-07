using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #13367 (ADR-0070 A3.4) — <c>tramites.consolidado_export_settings</c>: fila única global de parámetros del motor de
/// lotes. El índice <c>uq_consolidado_export_settings_singleton ON ((true))</c> no se puede expresar con
/// <c>HasIndex</c>; vive solo en SQL (133-HU13367-consolidado-export-batches.sql), igual que los CHECK de rango y de
/// coherencia lease &gt; timeout, como en <c>NotificationTestSettingsConfiguration</c>.
/// </summary>
internal sealed class ConsolidadoExportSettingsConfiguration : IEntityTypeConfiguration<ConsolidadoExportSettings>
{
    public void Configure(EntityTypeBuilder<ConsolidadoExportSettings> builder)
    {
        builder.ToTable("consolidado_export_settings", SchemaNames.Tramites, t =>
        {
            t.HasTrigger("tr_consolidado_export_settings_row_version");
            t.HasTrigger("tr_consolidado_export_settings_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_consolidado_export_settings");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.MaxPdfsPerPart).HasColumnName("max_pdfs_per_part");
        builder.Property(x => x.MaxMbPerPart).HasColumnName("max_mb_per_part");
        builder.Property(x => x.ItemSlots).HasColumnName("item_slots");
        builder.Property(x => x.ItemTimeoutSeconds).HasColumnName("item_timeout_seconds");
        builder.Property(x => x.ItemLeaseSeconds).HasColumnName("item_lease_seconds");
        builder.Property(x => x.MaxItemAttempts).HasColumnName("max_item_attempts");
        builder.Property(x => x.RetryDelaySeconds).HasColumnName("retry_delay_seconds");
        builder.Property(x => x.PartTimeoutSeconds).HasColumnName("part_timeout_seconds");
        builder.Property(x => x.PartLeaseSeconds).HasColumnName("part_lease_seconds");
        builder.Property(x => x.MaxPartAttempts).HasColumnName("max_part_attempts");
        builder.Property(x => x.RetentionHours).HasColumnName("retention_hours");
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        // Lo incrementa public.trg_row_version(); sin lectura de vuelta (ver 07-schema §5, concurrencia).
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
    }
}
