using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #13367 (ADR-0070, adendas v3 y v5) — <c>tramites.consolidado_export_batches</c>. Los CHECK (origen ↔ tenant,
/// origen ↔ organismo, contadores, máquina de estados), las FK a <c>identity.tenants</c>, <c>identity.users</c> y
/// <c>catalogs.transit_offices</c>, la política RLS decorativa y los triggers viven en SQL
/// (133-HU13367-consolidado-export-batches.sql). Aquí se declaran los índices para que el snapshot los refleje.
/// </summary>
internal sealed class ConsolidadoExportBatchConfiguration : IEntityTypeConfiguration<ConsolidadoExportBatch>
{
    internal const string ActiveFilter =
        "status IN ('en_cola', 'en_proceso', 'empaquetando') AND deleted_at IS NULL";

    public void Configure(EntityTypeBuilder<ConsolidadoExportBatch> builder)
    {
        builder.ToTable("consolidado_export_batches", SchemaNames.Tramites, t =>
        {
            t.HasTrigger("tr_consolidado_export_batches_row_version");
            t.HasTrigger("tr_consolidado_export_batches_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_consolidado_export_batches");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        // E5: NULL si y solo si origin = 'superadmin' (ck_consolidado_export_batches_tenant_origin).
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.RequestedRoleCode).HasColumnName("requested_role_code").HasColumnType("text").IsRequired();
        builder.Property(x => x.ScopeTenantId).HasColumnName("scope_tenant_id");
        // HU #13417 (adenda v7): DEFAULT false en el DDL; EF siempre envía el valor (el lote propio = false).
        // Los CHECK network_origin / network_child / scope_origin viven en SQL.
        builder.Property(x => x.NetworkScope).HasColumnName("network_scope").IsRequired();
        builder.Property(x => x.Origin).HasColumnName("origin").HasColumnType("text").IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasColumnType("text").IsRequired();
        builder.Property(x => x.SelectionMode).HasColumnName("selection_mode").HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text")
            .HasDefaultValue(ConsolidadoExportStatus.EnCola).IsRequired();

        builder.Property(x => x.TotalItems).HasColumnName("total_items").IsRequired();
        builder.Property(x => x.IncludedCount).HasColumnName("included_count");
        builder.Property(x => x.OmittedCount).HasColumnName("omitted_count");
        builder.Property(x => x.GeneratedCount).HasColumnName("generated_count");
        builder.Property(x => x.PartsCount).HasColumnName("parts_count");

        builder.Property(x => x.DekWrapped).HasColumnName("dek_wrapped").HasColumnType("bytea");
        builder.Property(x => x.EffectsAcknowledgedAt).HasColumnName("effects_acknowledged_at").IsRequired();
        builder.Property(x => x.LastClaimedAt).HasColumnName("last_claimed_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.PurgedAt).HasColumnName("purged_at");
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasColumnType("text");
        builder.Property(x => x.OtTransitOfficeId).HasColumnName("ot_transit_office_id");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        // Lo incrementa public.trg_row_version(); las transiciones del motor van por SQL con WHERE row_version = @v
        // o con el lock del lote (07-schema §5).
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        // CF-17: un lote activo por usuario. El 23505 sobre este nombre se traduce a 409 lote_activo.
        builder.HasIndex(x => x.RequestedByUserId)
            .HasDatabaseName("uq_consolidado_export_batches_active_per_user")
            .IsUnique()
            .HasFilter(ActiveFilter);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_consolidado_export_batches_tenant_created")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.RequestedByUserId, x.CreatedAt })
            .HasDatabaseName("ix_consolidado_export_batches_requested_by_created")
            .IsDescending(false, true);

        builder.HasIndex(x => x.ScopeTenantId)
            .HasDatabaseName("ix_consolidado_export_batches_scope_tenant")
            .HasFilter("scope_tenant_id IS NOT NULL");

        builder.HasIndex(x => x.OtTransitOfficeId)
            .HasDatabaseName("ix_consolidado_export_batches_transit_office")
            .HasFilter("ot_transit_office_id IS NOT NULL");

        builder.HasIndex(x => new { x.LastClaimedAt, x.CreatedAt })
            .HasDatabaseName("ix_consolidado_export_batches_claim")
            .HasFilter("status IN ('en_cola', 'en_proceso')");

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_consolidado_export_batches_purge")
            .HasFilter("purged_at IS NULL AND expires_at IS NOT NULL");
    }
}
