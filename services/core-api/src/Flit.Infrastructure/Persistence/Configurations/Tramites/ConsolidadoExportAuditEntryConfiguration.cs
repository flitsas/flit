using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #13368 (ADR-0070 D8, E2) — auditoría append-only Ley 1581 de los lotes de descarga masiva
/// (<c>tramites.consolidado_export_audit</c>). Sin FK ni navegaciones a propósito (la fila sobrevive al lote, al usuario
/// y a la compañía); sin soft delete. Los CHECK por evento, la política RLS del DDL 113
/// (<c>actor_tenant_id ∪ reached_tenant_ids</c>) y el trigger de inmutabilidad viven solo en SQL
/// (134-HU13368-consolidado-export-items.sql). Solo inserción: ningún repositorio expone update.
/// </summary>
internal sealed class ConsolidadoExportAuditEntryConfiguration : IEntityTypeConfiguration<ConsolidadoExportAuditEntry>
{
    public void Configure(EntityTypeBuilder<ConsolidadoExportAuditEntry> builder)
    {
        builder.ToTable("consolidado_export_audit", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_consolidado_export_audit_immutable"));

        builder.HasKey(x => x.Id).HasName("pk_consolidado_export_audit");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.Event).HasColumnName("event").HasColumnType("text").IsRequired();
        builder.Property(x => x.Origin).HasColumnName("origin").HasColumnType("text").IsRequired();
        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.ActorTenantId).HasColumnName("actor_tenant_id");
        builder.Property(x => x.ActorRoleCode).HasColumnName("actor_role_code").HasColumnType("text").IsRequired();
        builder.Property(x => x.ScopeTenantId).HasColumnName("scope_tenant_id");
        builder.Property(x => x.ReachedTenantIds).HasColumnName("reached_tenant_ids").HasColumnType("uuid[]");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasColumnType("text").IsRequired();
        builder.Property(x => x.SelectionMode).HasColumnName("selection_mode").HasColumnType("text");
        builder.Property(x => x.FilterSummary).HasColumnName("filter_summary").HasColumnType("jsonb");
        builder.Property(x => x.IdsCount).HasColumnName("ids_count");
        builder.Property(x => x.ExcludedCount).HasColumnName("excluded_count");
        builder.Property(x => x.TotalItems).HasColumnName("total_items");
        builder.Property(x => x.IncludedCount).HasColumnName("included_count");
        builder.Property(x => x.OmittedCount).HasColumnName("omitted_count");
        builder.Property(x => x.GeneratedCount).HasColumnName("generated_count");
        builder.Property(x => x.PartNumber).HasColumnName("part_number");
        builder.Property(x => x.ClientIp).HasColumnName("client_ip").HasColumnType("inet");
        builder.Property(x => x.UserAgent).HasColumnName("user_agent").HasColumnType("text");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.ActorTenantId, x.OccurredAt })
            .HasDatabaseName("ix_consolidado_export_audit_actor_tenant_occurred")
            .IsDescending(false, true);

        builder.HasIndex(x => x.BatchId).HasDatabaseName("ix_consolidado_export_audit_batch");

        // Compañías alcanzadas (Super Admin multicompañía) y policy RLS.
        builder.HasIndex(x => x.ReachedTenantIds)
            .HasDatabaseName("ix_consolidado_export_audit_reached_tenant_ids")
            .HasMethod("gin");
    }
}
